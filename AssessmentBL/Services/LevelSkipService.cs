using AssessmentBL.DTOs.LevelSkip;
using AssessmentBL.DTOs.QuizAttempt;
using AssessmentBL.Interfaces;
using AssessmentBL.Services.Constants;
using AssessmentDA.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shared.Common.Abstractions;
using Shared.Content;

namespace AssessmentBL.Services
{
    /// <summary>
    /// The level-skip challenge, from the app's point of view: may the child try,
    /// and start it. The rules of what it contains live in
    /// <see cref="LevelSkipEngine"/>, and running it is an ordinary quiz attempt.
    /// </summary>
    public sealed class LevelSkipService : ILevelSkipService
    {
        private readonly AssessmentDbContext _db;
        private readonly IQuizAttemptService _attempts;
        private readonly LevelSkipEngine _engine;
        private readonly QuizRules _rules;
        private readonly ILevelCatalog _levels;
        private readonly ILessonAvailability _lessons;
        private readonly IDateTimeProvider _clock;
        private readonly AssessmentSettings _settings;

        public LevelSkipService(
            AssessmentDbContext db,
            IQuizAttemptService attempts,
            LevelSkipEngine engine,
            QuizRules rules,
            ILevelCatalog levels,
            ILessonAvailability lessons,
            IDateTimeProvider clock,
            IOptions<AssessmentSettings> settings)
        {
            _db = db;
            _attempts = attempts;
            _engine = engine;
            _rules = rules;
            _levels = levels;
            _lessons = lessons;
            _clock = clock;
            _settings = settings.Value;
        }

        public async Task<LevelSkipStatusDto> GetStatusAsync(
            int levelId,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            var levels = await _levels.GetLevelsInOrderAsync(cancellationToken);

            if (!levels.Any(l => l.Id == levelId))
                throw new KeyNotFoundException($"المستوى رقم {levelId} غير موجود");

            var rules = _rules.For(QuizTypes.LevelSkip);

            var status = new LevelSkipStatusDto
            {
                LevelId = levelId,
                Status = LevelSkipStatuses.Unavailable,
                TimeLimitSeconds = rules.TimeLimitSeconds ?? 0,
                Hearts = rules.Hearts ?? 0,
                PassPercentage = _settings.EffectiveLevelSkipPassPercentage
            };

            // Nothing to skip: the child has already finished the level's lessons.
            var lessons = await _lessons.GetPublishedLessonsAsync(levelId, cancellationToken);

            var quizId = await _engine.FindActiveQuizIdAsync(levelId, cancellationToken);

            if (quizId is null)
                return status;

            status.QuizId = quizId;

            var attempts = await _db.QuizAttempts
                .AsNoTracking()
                .Where(a => a.UserId == userId && a.QuizId == quizId.Value)
                .Select(a => new
                {
                    a.Id,
                    a.Status,
                    a.StartedAt,
                    a.ScorePercentage,
                    WrongAnswers = a.QuestionsAnsweredCount - a.CorrectAnswersCount
                })
                .ToListAsync(cancellationToken);

            status.PreviousAttempts = attempts.Count(a => a.Status != QuizAttemptStatuses.InProgress);

            // Already skipped. Judged on the SAME two conditions the submission
            // applies — hearts survived AND the score reached the mark — rather
            // than on the score alone, which would report a run lost on hearts as
            // a pass whenever the two thresholds were configured apart.
            //
            // A level-skip attempt has no essays, so "answered − correct" is
            // exactly its wrong answers.
            var heartsAllowed = rules.Hearts;

            if (attempts.Any(a => a.Status == QuizAttemptStatuses.Completed
                               && a.ScorePercentage >= _settings.EffectiveLevelSkipPassPercentage
                               && (heartsAllowed is not byte hearts
                                   || a.WrongAnswers < hearts)))
            {
                status.Status = LevelSkipStatuses.Passed;
                return status;
            }

            var cutoff = _settings.AbandonCutoff(_clock.UtcNow);

            var open = attempts
                .Where(a => a.Status == QuizAttemptStatuses.InProgress
                         && a.StartedAt > cutoff
                         && !rules.HasRunOut(a.StartedAt, _clock.UtcNow))
                .OrderByDescending(a => a.Id)
                .FirstOrDefault();

            if (open is not null)
            {
                status.Status = LevelSkipStatuses.InProgress;
                status.AttemptId = open.Id;
                status.QuestionCount = await _db.QuizAttemptQuestions
                    .AsNoTracking()
                    .CountAsync(aq => aq.QuizAttemptId == open.Id, cancellationToken);

                return status;
            }

            if (lessons.Count == 0)
                return status;

            // Asking the engine is the only honest answer to "can this start?":
            // the level may publish lessons whose quizzes are all drafts.
            var questionIds = await _engine.SelectQuestionIdsAsync(levelId, userId, cancellationToken);

            if (questionIds.Count == 0)
                return status;

            status.Status = LevelSkipStatuses.Available;
            status.QuestionCount = questionIds.Count;

            return status;
        }

        public async Task<QuizAttemptResponseDto> StartAsync(
            int levelId,
            Guid userId,
            string? language = null,
            CancellationToken cancellationToken = default)
        {
            var quizId = await _engine.FindActiveQuizIdAsync(levelId, cancellationToken)
                ?? throw new KeyNotFoundException($"لا يوجد اختبار تخطي متاح للمستوى رقم {levelId}");

            // The attempt engine owns the rules for starting: it resumes an open
            // challenge rather than creating a second one, and samples the level.
            return await _attempts.StartAsync(quizId, userId, previousAttemptId: null, language, cancellationToken);
        }
    }
}
