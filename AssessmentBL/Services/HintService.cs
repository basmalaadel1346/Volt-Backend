using AssessmentBL.DTOs.QuizAttempt;
using AssessmentBL.Interfaces;
using AssessmentBL.Services.Constants;
using AssessmentDA.Context;
using AssessmentDA.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Assessment.AI;
using Shared.Common.Abstractions;
using Shared.Common.Exceptions;
using Shared.Users;

namespace AssessmentBL.Services
{
    /// <summary>
    /// The Hint button (addendum v1.1 §D): while an attempt is in progress, the
    /// child may ask for a hint on a question, twice. The first is a soft nudge,
    /// the second more direct; a third press is refused.
    ///
    /// The level is derived from the hints this attempt+question already has, so
    /// the client cannot ask for the direct hint first. Neither level may name the
    /// correct answer: both go through the same leak checks, and a level-2 hint
    /// that fails them is dropped exactly like a level-1 one.
    ///
    /// Only a hint the child actually sees uses a level. A Partial or Unavailable
    /// press saves nothing, so it is not counted against the child: the next press
    /// asks for the same level again, and the hint statistics never see it.
    ///
    /// Two presses at the same moment both read the same level. The database
    /// settles it: UQ_QuestionHints_AttemptId_QuestionId_AttemptNumber lets one
    /// save that level, in any language, and the other gets a 409 with nothing saved.
    ///
    /// Nothing here touches scoring: hints are written to their own table and the
    /// submit pipeline is unchanged.
    /// </summary>
    public class HintService : IHintService
    {
        private readonly AssessmentDbContext _db;
        private readonly IAiHintButton _ai;
        private readonly AiRequestBuilder _aiRequests;
        private readonly ILearnerProfile _learners;
        private readonly IDateTimeProvider _clock;
        private readonly AssessmentSettings _settings;
        private readonly ILogger<HintService> _logger;

        public HintService(
            AssessmentDbContext db,
            IAiHintButton ai,
            AiRequestBuilder aiRequests,
            ILearnerProfile learners,
            IDateTimeProvider clock,
            IOptions<AssessmentSettings> settings,
            ILogger<HintService> logger)
        {
            _db = db;
            _ai = ai;
            _aiRequests = aiRequests;
            _learners = learners;
            _clock = clock;
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task<HintResponseDto> RequestHintAsync(
            long attemptId,
            int questionId,
            Guid userId,
            string? language = null,
            CancellationToken cancellationToken = default)
        {
            var resolvedLanguage = ContentLanguages.Normalize(language);

            // Filtered on the owner: another user's attempt is reported exactly like
            // one that does not exist, so a 404 never confirms that an id is real.
            var attempt = await _db.QuizAttempts
                .AsNoTracking()
                .Where(a => a.Id == attemptId && a.UserId == userId)
                .Select(a => new { a.Status, a.StartedAt, a.Quiz.QuizType })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException($"المحاولة رقم {attemptId} غير موجودة");

            if (attempt.Status == QuizAttemptStatuses.Abandoned
                || (attempt.Status == QuizAttemptStatuses.InProgress
                    && attempt.StartedAt <= _settings.AbandonCutoff(_clock.UtcNow)))
                throw new GoneException(
                    $"المحاولة رقم {attemptId} انتهت صلاحيتها قبل تسليمها، برجاء بدء محاولة جديدة");

            if (attempt.Status != QuizAttemptStatuses.InProgress)
                throw new ConflictException(
                    $"المحاولة رقم {attemptId} تم تسليمها، ولا يمكن طلب تلميح بعد التسليم");

            // The placement test measures what the child already knows, and places
            // them once and for all: a hint there would seat them above their level.
            // The post-submission hints skip placement for the same reason.
            if (attempt.QuizType == QuizTypes.Placement)
                throw new ConflictException("اختبار تحديد المستوى لا يحتوي على تلميحات");

            // The frozen snapshot is the authority on what this attempt asked and
            // on the answer key — never the live question.
            var snapshot = await _db.QuizAttemptQuestions
                .AsNoTracking()
                .Where(aq => aq.QuizAttemptId == attemptId && aq.QuestionId == questionId)
                .Select(aq => new { aq.QuestionType, aq.CorrectOptionId })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException(
                    $"السؤال رقم {questionId} ليس ضمن المحاولة رقم {attemptId}");

            // Escalation level = hints already given for this question in this
            // attempt. Derived here, never sent by the client — and counted across
            // ALL languages, or asking again in another language would hand out the
            // two levels a second time.
            var given = await _db.QuestionHints
                .AsNoTracking()
                .CountAsync(h => h.QuizAttemptId == attemptId
                              && h.QuestionId == questionId
                              && h.AttemptNumber != null, cancellationToken);

            if (given >= _settings.EffectiveMaxHintLevels)
                throw new ConflictException(
                    $"لا توجد تلميحات إضافية للسؤال رقم {questionId} في هذه المحاولة");

            var attemptNumber = (byte)(given + 1);

            if (!_ai.IsConfigured)
                return NoHint(questionId, attemptNumber, resolvedLanguage, HintStatuses.Unavailable);

            var row = (await LocalizedQuestionQuery.Project(
                        _db.Questions.AsNoTracking().Where(q => q.Id == questionId), resolvedLanguage)
                    .ToListAsync(cancellationToken))
                .FirstOrDefault()
                ?? throw new KeyNotFoundException($"السؤال رقم {questionId} غير موجود");

            var isEssay = !QuestionTypes.IsAutoGraded(snapshot.QuestionType);
            var correct = snapshot.CorrectOptionId is int correctOptionId
                ? row.Options.FirstOrDefault(o => o.OptionId == correctOptionId)
                : null;

            // An auto-graded question the AI cannot interpret gets no hint rather
            // than a guess: same rule as the post-submit hints.
            if (!isEssay && correct is null)
                return NoHint(questionId, attemptNumber, resolvedLanguage, HintStatuses.Unavailable);

            // What the correct answer READS as — its text, or the description
            // standing in for an image-only option. Without it the leak checks have
            // nothing to compare against and would wave everything through.
            var correctAnswerText = correct is null
                ? null
                : AiRequestBuilder.OptionSemanticText(correct.OptionText, correct.ImageDescription);

            if (!isEssay && correctAnswerText is null)
            {
                _logger.LogWarning(
                    "No hint for question {QuestionId}: the correct option has neither text nor an ImageDescription, "
                  + "so a hint could not be checked for giving it away.", questionId);
                return NoHint(questionId, attemptNumber, resolvedLanguage, HintStatuses.Unavailable);
            }

            if (AiRequestBuilder.QuestionSemanticText(row.QuestionText, row.ImageDescription) is null)
            {
                _logger.LogWarning(
                    "No hint for question {QuestionId}: it has neither text nor an ImageDescription.", questionId);
                return NoHint(questionId, attemptNumber, resolvedLanguage, HintStatuses.Unavailable);
            }

            var hint = await TryGenerateAsync(
                attemptId, questionId, attemptNumber, userId, resolvedLanguage, row, snapshot.QuestionType, correct,
                correctAnswerText, cancellationToken);

            if (hint is null)
                return NoHint(questionId, attemptNumber, resolvedLanguage, HintStatuses.Partial);

            await SaveAsync(attemptId, questionId, attemptNumber, hint, resolvedLanguage, cancellationToken);

            return new HintResponseDto
            {
                QuestionId = questionId,
                AttemptNumber = attemptNumber,
                Hint = hint,
                HintsStatus = HintStatuses.Generated,
                HintsRemaining = HintsRemaining(_settings.EffectiveMaxHintLevels, attemptNumber, HintStatuses.Generated),
                Language = resolvedLanguage
            };
        }

        /// <summary>
        /// Returns the accepted hint text, or null when the AI failed or its answer
        /// would have given the correct option away. Never throws for AI problems.
        /// </summary>
        private async Task<string?> TryGenerateAsync(
            long attemptId,
            int questionId,
            byte attemptNumber,
            Guid userId,
            string language,
            LocalizedQuestionRow row,
            string questionType,
            LocalizedOptionRow? correct,
            string? correctAnswerText,
            CancellationToken cancellationToken)
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(_settings.AiHintTimeout);

            HintResponse? response;

            try
            {
                var imageBudget = _aiRequests.NewImageBudget();

                // Age only, and only when it is known (Shared contract, Users module).
                var age = await _learners.GetAgeAsync(userId, budget.Token);

                var previousHints = await _db.QuestionHints
                    .AsNoTracking()
                    .Where(h => h.QuizAttemptId == attemptId
                             && h.QuestionId == questionId
                             && h.LanguageCode == language)
                    .OrderBy(h => h.HintSequence)
                    .Select(h => h.HintText)
                    .ToListAsync(budget.Token);

                var options = new List<AiOption>(row.Options.Count);

                foreach (var option in row.Options)
                    options.Add(new AiOption
                    {
                        OptionId = option.OptionId,
                        Text = AiRequestBuilder.Clean(option.OptionText),
                        Image = await _aiRequests.BuildImageAsync(
                            option.ImageUrl, option.ImageDescription, imageBudget, budget.Token)
                    });

                response = await _ai.RequestHintAsync(new HintRequest
                {
                    RequestId = Guid.NewGuid(),
                    AttemptNumber = attemptNumber,
                    Language = language,
                    LearnerContext = age is int years ? new LearnerContext { Age = years } : null,
                    Question = new HintQuestion
                    {
                        Text = AiRequestBuilder.Clean(row.QuestionText),
                        Type = questionType,
                        Image = await _aiRequests.BuildImageAsync(
                            row.ImageUrl, row.ImageDescription, imageBudget, budget.Token),
                        Options = options,
                        // Essay has no key at hint time — grading is asynchronous.
                        CorrectAnswer = correct is null
                            ? null
                            : new HintCorrectAnswer
                            {
                                OptionId = correct.OptionId,
                                Text = AiRequestBuilder.Clean(correct.OptionText)
                            }
                    },
                    PreviousHints = previousHints
                }, budget.Token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "The AI could not produce hint {AttemptNumber} for question {QuestionId} of attempt {AttemptId}.",
                    attemptNumber, questionId, attemptId);
                return null;
            }

            if (response?.Status is not null
                && !string.Equals(response.Status, AiResultStatuses.Ok, StringComparison.OrdinalIgnoreCase))
                return null;

            var hint = response?.Hint?.Trim();

            if (string.IsNullOrEmpty(hint) || hint.Length > _settings.EffectiveMaxHintLength)
                return null;

            // An Essay has no correct option to leak. For everything else BOTH
            // levels face the same checks — level 2 is more direct, never looser.
            if (correct is null || correctAnswerText is null)
                return hint;

            var isBinary = questionType == QuestionTypes.TrueFalse;
            var otherOption = row.Options.FirstOrDefault(o => o.OptionId != correct.OptionId);

            var otherText = otherOption is null
                ? null
                : AiRequestBuilder.OptionSemanticText(otherOption.OptionText, otherOption.ImageDescription);

            if (HintSafety.RevealsAnswer(hint, correctAnswerText, isBinary, otherText)
                || (!isBinary && HintSafety.IsTooSimilar(hint, correctAnswerText, _settings.EffectiveHintSimilarityThreshold)))
            {
                _logger.LogWarning(
                    "Dropped hint {AttemptNumber} for question {QuestionId}: it gives the correct answer away.",
                    attemptNumber, questionId);
                return null;
            }

            return hint;
        }

        private async Task SaveAsync(
            long attemptId,
            int questionId,
            byte attemptNumber,
            string hint,
            string language,
            CancellationToken cancellationToken)
        {
            var lastSequence = await _db.QuestionHints
                .AsNoTracking()
                .Where(h => h.QuizAttemptId == attemptId
                         && h.QuestionId == questionId
                         && h.LanguageCode == language)
                .Select(h => (byte?)h.HintSequence)
                .MaxAsync(cancellationToken);

            _db.QuestionHints.Add(new QuestionHint
            {
                QuizAttemptId = attemptId,
                QuestionId = questionId,
                QuizAttemptMistakeId = null,   // asked for mid-attempt: no mistake row yet
                HintText = hint,
                HintSequence = (byte)((lastSequence ?? 0) + 1),
                AttemptNumber = attemptNumber,
                LanguageCode = language
            });

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (
                ex.IsUniqueViolationOf("UQ_QuestionHints_AttemptId_QuestionId_AttemptNumber")
             || ex.IsUniqueViolationOf("UQ_QuestionHints_AttemptId_QuestionId_Language_Sequence"))
            {
                // Two presses at once — in one language or in two — both computed
                // this level. The other one is already saved and is the hint of this
                // level; this one saved nothing and used no level.
                _db.ChangeTracker.Clear();
                throw new ConflictException("تم طلب تلميح لنفس السؤال بالفعل، برجاء المحاولة مرة أخرى", ex);
            }
        }

        private HintResponseDto NoHint(int questionId, byte attemptNumber, string language, string status) =>
            new()
            {
                QuestionId = questionId,
                AttemptNumber = attemptNumber,
                Hint = null,
                HintsStatus = status,
                HintsRemaining = HintsRemaining(_settings.EffectiveMaxHintLevels, attemptNumber, status),
                Language = language
            };

        /// <summary>
        /// Hint-button levels left for the question after this press. Only a
        /// Generated hint — one the child sees — uses its level; after a Partial or
        /// Unavailable press the count is what it was before.
        /// </summary>
        public static int HintsRemaining(int maxLevels, byte attemptNumber, string hintsStatus)
        {
            var used = hintsStatus == HintStatuses.Generated ? attemptNumber : attemptNumber - 1;
            return Math.Max(maxLevels - used, 0);
        }
    }
}
