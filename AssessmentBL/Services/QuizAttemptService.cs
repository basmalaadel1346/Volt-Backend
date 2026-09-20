using AssessmentBL.DTOs.Placement;
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
using Shared.Content;
using Shared.Users;

namespace AssessmentBL.Services
{
    public class QuizAttemptService : IQuizAttemptService
    {
        // Rows flipped per UPDATE by the abandoned-attempt sweep, so one run never
        // holds a long lock on QuizAttempts however large the backlog is.
        private const int AbandonSweepBatchSize = 500;

        private const string AlreadyPlacedMessage = "تم تحديد مستواك بالفعل، لا يمكن إعادة اختبار تحديد المستوى";

        private static readonly IReadOnlyDictionary<int, string> NoHints = new Dictionary<int, string>();

        private readonly AssessmentDbContext _db;
        private readonly IUserTopicStatService _userTopicStatService;
        private readonly IDateTimeProvider _clock;
        private readonly IAiHintGenerator _aiHintGenerator;
        private readonly IEssayEvaluationService _essays;
        private readonly AiRequestBuilder _aiRequests;
        private readonly ILearnerProfile _learners;
        private readonly ILessonAvailability _lessons;
        private readonly PlacementEngine _placement;
        private readonly AssessmentSettings _settings;
        private readonly ILogger<QuizAttemptService> _logger;

        public QuizAttemptService(
            AssessmentDbContext db,
            IUserTopicStatService userTopicStatService,
            IDateTimeProvider clock,
            IAiHintGenerator aiHintGenerator,
            IEssayEvaluationService essays,
            AiRequestBuilder aiRequests,
            ILearnerProfile learners,
            ILessonAvailability lessons,
            PlacementEngine placement,
            IOptions<AssessmentSettings> settings,
            ILogger<QuizAttemptService> logger)
        {
            _db = db;
            _userTopicStatService = userTopicStatService;
            _clock = clock;
            _aiHintGenerator = aiHintGenerator;
            _essays = essays;
            _aiRequests = aiRequests;
            _learners = learners;
            _lessons = lessons;
            _placement = placement;
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task<QuizAttemptResponseDto> StartAsync(
            int quizId,
            Guid userId,
            long? previousAttemptId = null,
            string? language = null,
            CancellationToken cancellationToken = default)
        {
            var resolvedLanguage = ContentLanguages.Normalize(language);

            var quiz = await _db.Quizzes
                .AsNoTracking()
                .Where(q => q.Id == quizId)
                .Select(q => new { q.IsActive, q.QuizType, q.LessonId })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException($"الاختبار رقم {quizId} غير موجود");

            if (!quiz.IsActive)
                throw new BusinessRuleException($"الاختبار رقم {quizId} غير مفعّل");

            // A lesson's quiz is open only while the lesson is visible to learners —
            // the same rule GET /api/quizzes/for-lesson applies.
            if (quiz.LessonId is int lessonId
                && await _lessons.IsPublishedAsync(lessonId, cancellationToken) != true)
                throw new KeyNotFoundException($"الاختبار رقم {quizId} غير متاح");

            if (quiz.QuizType == QuizTypes.Placement)
            {
                if (previousAttemptId is not null)
                    throw new BusinessRuleException("اختبار تحديد المستوى لا تتم إعادته");

                return await StartPlacementAttemptAsync(quizId, userId, resolvedLanguage, cancellationToken);
            }

            return previousAttemptId is null
                ? await StartFirstAttemptAsync(quizId, userId, resolvedLanguage, cancellationToken)
                : await StartRetryAttemptAsync(quizId, userId, previousAttemptId.Value, resolvedLanguage, cancellationToken);
        }

        private async Task<QuizAttemptResponseDto> StartFirstAttemptAsync(
            int quizId,
            Guid userId,
            string language,
            CancellationToken cancellationToken)
        {
            var rows = await LocalizedQuestionQuery.Project(
                    _db.Questions.AsNoTracking().Where(q => q.QuizId == quizId && q.IsActive), language)
                .ToListAsync(cancellationToken);

            if (rows.Count == 0)
                throw new BusinessRuleException(
                    $"الاختبار رقم {quizId} لا يحتوي على أسئلة مفعّلة");

            var snapshots = await LoadQuestionSnapshotsAsync(
                rows.Select(r => r.QuestionId).ToList(), cancellationToken);

            return await PersistAttemptAsync(
                quizId, userId, previousAttemptId: null, rows, snapshots, language, cancellationToken);
        }

        private async Task<QuizAttemptResponseDto> StartRetryAttemptAsync(
            int quizId,
            Guid userId,
            long previousAttemptId,
            string language,
            CancellationToken cancellationToken)
        {
            // Filtered on the owner: another user's attempt is reported exactly like
            // one that does not exist, so a 404 never confirms that an id is real.
            var previousAttempt = await _db.QuizAttempts
                .AsNoTracking()
                .Where(a => a.Id == previousAttemptId && a.UserId == userId)
                .Select(a => new { a.Id, a.QuizId, a.Status })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException($"المحاولة رقم {previousAttemptId} غير موجودة");

            if (previousAttempt.QuizId != quizId)
                throw new BusinessRuleException(
                    $"المحاولة رقم {previousAttemptId} لا تخص الاختبار رقم {quizId}");

            if (previousAttempt.Status != QuizAttemptStatuses.Completed)
                throw new BusinessRuleException(
                    $"لا يمكن إعادة المحاولة رقم {previousAttemptId} لأنها لم تكتمل");

            var alreadyRetried = await _db.QuizAttempts
                .AsNoTracking()
                .AnyAsync(a => a.PreviousAttemptId == previousAttemptId, cancellationToken);

            if (alreadyRetried)
                throw new BusinessRuleException(
                    $"تمت إعادة المحاولة رقم {previousAttemptId} من قبل");

            // Only auto-graded questions can produce a mistake, so a retry never
            // contains an Essay by construction.
            var wrongQuestionIds = await _db.QuizAttemptMistakes
                .AsNoTracking()
                .Where(m => m.QuizAttemptId == previousAttemptId)
                .Select(m => m.QuestionId)
                .ToListAsync(cancellationToken);

            if (wrongQuestionIds.Count == 0)
                throw new BusinessRuleException(
                    $"المحاولة رقم {previousAttemptId} لا تحتوي على إجابات خاطئة لإعادتها");

            // A question an admin has deactivated since (typically because it was
            // wrong) is not served again.
            var rows = await LocalizedQuestionQuery.Project(
                    _db.Questions.AsNoTracking().Where(q => wrongQuestionIds.Contains(q.Id) && q.IsActive), language)
                .ToListAsync(cancellationToken);

            if (rows.Count == 0)
                throw new BusinessRuleException(
                    $"أسئلة المحاولة رقم {previousAttemptId} الخاطئة لم تعد متاحة لإعادتها");

            var latestHints = await GetLatestHintPerQuestionAsync(previousAttemptId, language, cancellationToken);

            foreach (var row in rows)
            {
                if (latestHints.TryGetValue(row.QuestionId, out var hintText))
                    row.CurrentHint = hintText;
            }

            var snapshots = await LoadQuestionSnapshotsAsync(
                rows.Select(r => r.QuestionId).ToList(), cancellationToken);

            return await PersistAttemptAsync(
                quizId, userId, previousAttemptId, rows, snapshots, language, cancellationToken);
        }

        /// <summary>
        /// The placement test. Refuses a learner who is already placed, resumes an
        /// open placement attempt (the app was closed mid-test), and otherwise
        /// serves the level-by-level sample chosen by <see cref="PlacementEngine"/>.
        /// </summary>
        private async Task<QuizAttemptResponseDto> StartPlacementAttemptAsync(
            int quizId,
            Guid userId,
            string language,
            CancellationToken cancellationToken)
        {
            if (await _db.UserPlacements.AsNoTracking().AnyAsync(p => p.UserId == userId, cancellationToken))
                throw new ConflictException(AlreadyPlacedMessage);

            var openAttemptId = await _placement.FindResumableAttemptIdAsync(userId, quizId, cancellationToken);

            if (openAttemptId is long attemptId)
                return await GetByIdAsync(attemptId, userId, language, cancellationToken);

            var questionIds = await _placement.SelectQuestionIdsAsync(cancellationToken);

            if (questionIds.Count == 0)
                throw new BusinessRuleException(
                    "اختبار تحديد المستوى غير متاح حاليًا: لا توجد أسئلة تقييم مفعّلة للمستويات");

            var rows = await LoadRowsInOrderAsync(questionIds, language, cancellationToken);
            NumberInAttemptOrder(rows);

            var snapshots = await LoadQuestionSnapshotsAsync(questionIds, cancellationToken);

            return await PersistAttemptAsync(
                quizId, userId, previousAttemptId: null, rows, snapshots, language, cancellationToken);
        }

        /// <summary>
        /// Loads the localized rows for <paramref name="orderedQuestionIds"/> and
        /// returns them in that order, not in DisplayOrder.
        /// </summary>
        private async Task<List<LocalizedQuestionRow>> LoadRowsInOrderAsync(
            List<int> orderedQuestionIds,
            string language,
            CancellationToken cancellationToken)
        {
            var rowsById = (await LocalizedQuestionQuery.Project(
                        _db.Questions.AsNoTracking().Where(q => orderedQuestionIds.Contains(q.Id)), language)
                    .ToListAsync(cancellationToken))
                .ToDictionary(r => r.QuestionId);

            return orderedQuestionIds
                .Where(rowsById.ContainsKey)
                .Select(id => rowsById[id])
                .ToList();
        }

        /// <summary>
        /// Placement questions come from several quizzes whose DisplayOrder values
        /// overlap (each level's quiz starts at 1), so the attempt's own order —
        /// easiest level first — is published as 1..n instead.
        /// </summary>
        private static void NumberInAttemptOrder(IReadOnlyList<LocalizedQuestionRow> rows)
        {
            for (var i = 0; i < rows.Count; i++)
                rows[i].DisplayOrder = (short)(i + 1);
        }

        /// <summary>
        /// Reads the question fields that must be frozen for the lifetime of an
        /// attempt: its classification, its type, its answer key and its Points.
        /// An Essay has no key, so CorrectOptionId is null for it — the live
        /// CK_QuizAttemptQuestions_EssayHasNoKey requires exactly that: NULL for
        /// Essay and NOT NULL for every other type.
        /// </summary>
        private async Task<Dictionary<int, QuestionSnapshot>> LoadQuestionSnapshotsAsync(
            IReadOnlyCollection<int> questionIds,
            CancellationToken cancellationToken)
        {
            var rows = await _db.Questions
                .AsNoTracking()
                .Where(q => questionIds.Contains(q.Id))
                .Select(q => new
                {
                    q.Id,
                    q.TopicId,
                    q.Difficulty,
                    q.QuestionType,
                    q.Points,
                    CorrectOptionId = q.QuestionOptions
                        .Where(o => o.IsCorrect)
                        .Select(o => (int?)o.Id)
                        .FirstOrDefault()
                })
                .ToListAsync(cancellationToken);

            var unanswerable = rows
                .Where(r => r.CorrectOptionId is null && QuestionTypes.IsAutoGraded(r.QuestionType))
                .Select(r => r.Id)
                .ToList();

            if (unanswerable.Count > 0)
                throw new BusinessRuleException(
                    $"لا يمكن بدء المحاولة: الأسئلة أرقام {string.Join(", ", unanswerable)} ليس لها إجابة صحيحة");

            return rows.ToDictionary(
                r => r.Id,
                r => new QuestionSnapshot(
                    r.TopicId,
                    r.Difficulty,
                    r.QuestionType,
                    QuestionTypes.IsAutoGraded(r.QuestionType) ? r.CorrectOptionId : null,
                    r.Points));
        }

        private sealed record QuestionSnapshot(
            int? TopicId, string Difficulty, string QuestionType, int? CorrectOptionId, byte Points);

        private async Task<QuizAttemptResponseDto> PersistAttemptAsync(
            int quizId,
            Guid userId,
            long? previousAttemptId,
            IReadOnlyList<LocalizedQuestionRow> rows,
            IReadOnlyDictionary<int, QuestionSnapshot> snapshots,
            string language,
            CancellationToken cancellationToken)
        {
            var attempt = new QuizAttempt
            {
                QuizId = quizId,
                UserId = userId,
                PreviousAttemptId = previousAttemptId,
                TotalQuestionsAtAttempt = (short)rows.Count,
                QuestionsAnsweredCount = 0,
                CorrectAnswersCount = 0,
                ScorePercentage = 0m,
                Status = QuizAttemptStatuses.InProgress,
                StartedAt = _clock.UtcNow
            };

            foreach (var row in rows)
            {
                var snapshot = snapshots[row.QuestionId];

                attempt.QuizAttemptQuestions.Add(new QuizAttemptQuestion
                {
                    QuestionId = row.QuestionId,
                    TopicId = snapshot.TopicId,
                    Difficulty = snapshot.Difficulty,
                    QuestionType = snapshot.QuestionType,
                    CorrectOptionId = snapshot.CorrectOptionId,
                    Points = snapshot.Points
                });

                // The rows were read in a separate query; show the child the weight
                // this attempt will actually count.
                row.Points = snapshot.Points;
            }

            _db.QuizAttempts.Add(attempt);

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (previousAttemptId is not null
                                               && ex.IsUniqueViolationOf("UQ_QuizAttempts_PreviousAttemptId"))
            {
                throw new ConflictException(
                    $"تمت إعادة المحاولة رقم {previousAttemptId} بالفعل", ex);
            }

            return new QuizAttemptResponseDto
            {
                AttemptId = attempt.Id,
                QuizId = attempt.QuizId,
                StartedAt = attempt.StartedAt,
                Language = language,
                LanguageFallbackApplied = rows.Any(r => r.UsedFallback),
                Questions = rows.Select(r => r.ToDto()).ToList()
            };
        }

        /// <summary>
        /// Submission is two phases with a hard boundary between them:
        ///
        ///   A. CRITICAL — validate (every question answered), grade, persist
        ///      answers + score + statistics (+ the placement, for a placement
        ///      test), COMMIT. No AI here.
        ///   B. OPTIONAL — only after the commit, within one AI budget: hints for
        ///      the wrong MCQ/TF answers, then AI evaluation of the essays. Nothing
        ///      here can fail the request or touch the committed score.
        ///
        /// A repeated submit of an already-completed attempt replays the saved
        /// result: no re-grading, no second statistics update, no second AI call.
        /// </summary>
        public async Task<QuizAttemptResultDto> SubmitAsync(
            long attemptId,
            Guid userId,
            SubmitQuizAttemptDto dto,
            string? language = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(dto);

            var resolvedLanguage = ContentLanguages.Normalize(language);

            // ── Phase A: critical ────────────────────────────────────────────
            var graded = await GradeAndCommitAsync(attemptId, userId, dto, resolvedLanguage, cancellationToken);

            // Already submitted — a double tap, a Flutter retry after a lost
            // response, or the losing side of a concurrent submit.
            if (graded is null)
                return await GetResultAsync(attemptId, userId, resolvedLanguage, cancellationToken);

            // ── Phase B: optional ────────────────────────────────────────────
            // Phase A's entities are committed; nothing below may re-save them.
            _db.ChangeTracker.Clear();

            // One budget for all optional AI work: hints first (they feed the
            // retry), then essays with whatever is left — essays not evaluated in
            // time are picked up by the background evaluator. Deliberately NOT the
            // request's token: if the child's connection drops right after the
            // commit, the work still finishes and GET .../result shows it.
            using var aiBudget = new CancellationTokenSource(_settings.AiHintTimeout);

            // A placement test is never retried, so there is nothing to hint.
            var retry = graded.Placement is null
                ? await BuildRetryQuestionsWithHintsAsync(userId, graded, resolvedLanguage, aiBudget.Token)
                : RetrySet.None();

            if (graded.EssayCount > 0)
                await TryEvaluateEssaysAsync(graded.AttemptId, aiBudget.Token);

            // Read back AFTER the essays had their chance, through the same loader
            // GET .../result uses, so this response and every later one count the
            // same points.
            var saved = await TryLoadSavedGradingAsync(graded.AttemptId);

            // Only if that read failed: what Phase A committed, every essay Pending.
            var points = saved?.Points ?? graded.Points;

            return new QuizAttemptResultDto
            {
                AttemptId = graded.AttemptId,
                QuizId = graded.QuizId,
                CompletedAt = graded.CompletedAt,
                TotalQuestions = graded.TotalQuestions,
                AutoGradedQuestions = graded.AutoGradedQuestions,
                PendingEssayQuestions = saved?.PendingEssayQuestions ?? graded.EssayCount,
                EssayResults = saved?.EssayResults ?? [],
                CorrectAnswers = graded.CorrectAnswers,
                WrongAnswers = (short)graded.Mistakes.Count,
                ScorePercentage = graded.ScorePercentage,
                TotalPoints = points.TotalPoints,
                EarnedPoints = points.EarnedPoints,
                PendingPoints = points.PendingPoints,
                Language = resolvedLanguage,
                LanguageFallbackApplied = retry.UsedFallback,
                HintsStatus = retry.HintsStatus,
                RetryQuestions = retry.Questions,
                Placement = graded.Placement
            };
        }

        /// <summary>
        /// Phase A. Returns null when the attempt had already been submitted, so
        /// the caller replays the saved result instead of grading twice.
        /// </summary>
        private async Task<GradedSubmission?> GradeAndCommitAsync(
            long attemptId,
            Guid userId,
            SubmitQuizAttemptDto dto,
            string language,
            CancellationToken cancellationToken)
        {
            // Filtered on the owner: another user's attempt is reported exactly like
            // one that does not exist, so a 404 never confirms that an id is real.
            var attempt = await _db.QuizAttempts
                .FirstOrDefaultAsync(a => a.Id == attemptId && a.UserId == userId, cancellationToken)
                ?? throw new KeyNotFoundException($"المحاولة رقم {attemptId} غير موجودة");

            if (attempt.Status == QuizAttemptStatuses.Completed)
                return null;

            if (attempt.Status == QuizAttemptStatuses.Abandoned)
                throw AttemptAbandoned(attemptId);

            if (IsExpired(attempt.StartedAt))
            {
                // The same rule the sweep applies, applied now, so the outcome
                // never depends on when the sweep last ran.
                await MarkAbandonedAsync(attemptId, cancellationToken);
                throw AttemptAbandoned(attemptId);
            }

            var quizType = await _db.Quizzes
                .AsNoTracking()
                .Where(q => q.Id == attempt.QuizId)
                .Select(q => q.QuizType)
                .FirstAsync(cancellationToken);

            var isPlacement = quizType == QuizTypes.Placement;

            if (isPlacement
                && await _db.UserPlacements.AsNoTracking().AnyAsync(p => p.UserId == userId, cancellationToken))
                throw new ConflictException(AlreadyPlacedMessage);

            // The attempt-start snapshot is the answer key, the type authority AND
            // the weight of every question — never the live Question.Points.
            var attemptQuestions = await _db.QuizAttemptQuestions
                .AsNoTracking()
                .Where(aq => aq.QuizAttemptId == attemptId)
                .Select(aq => new { aq.QuestionId, aq.CorrectOptionId, aq.QuestionType, aq.Points })
                .ToListAsync(cancellationToken);

            var autoGradedByQuestion = attemptQuestions
                .Where(aq => QuestionTypes.IsAutoGraded(aq.QuestionType))
                .ToDictionary(aq => aq.QuestionId, aq => aq.CorrectOptionId!.Value);

            var essayQuestionIds = attemptQuestions
                .Where(aq => !QuestionTypes.IsAutoGraded(aq.QuestionType))
                .Select(aq => aq.QuestionId)
                .ToHashSet();

            // An essay's maximum is its Points frozen at attempt start, so a later
            // change to the question cannot put a grade above the maximum shown.
            var essayMaxPoints = attemptQuestions
                .Where(aq => !QuestionTypes.IsAutoGraded(aq.QuestionType))
                .ToDictionary(aq => aq.QuestionId, aq => aq.Points);

            var submittedAnswers = ValidateSubmittedAnswers(dto, autoGradedByQuestion.Keys.ToHashSet());
            var submittedEssays = ValidateSubmittedEssays(dto, essayQuestionIds, _settings.EffectiveEssayAnswerMaxLength);

            EnsureEveryQuestionAnswered(
                attemptQuestions.Select(aq => aq.QuestionId),
                submittedAnswers.Select(a => a.QuestionId).Concat(submittedEssays.Select(e => e.QuestionId)).ToHashSet());

            var selectedOptionIds = submittedAnswers.Select(m => m.SelectedOptionId).ToList();

            var selectedOptions = await _db.QuestionOptions
                .AsNoTracking()
                .Where(o => selectedOptionIds.Contains(o.Id))
                .Select(o => new { o.Id, o.QuestionId })
                .ToListAsync(cancellationToken);

            var optionsById = selectedOptions.ToDictionary(o => o.Id);
            var confirmedMistakes = new List<QuizAttemptMistakeDto>(submittedAnswers.Count);

            foreach (var answer in submittedAnswers)
            {
                if (!optionsById.TryGetValue(answer.SelectedOptionId, out var option))
                    throw new ArgumentException(
                        $"الاختيار رقم {answer.SelectedOptionId} غير موجود", nameof(dto));

                if (option.QuestionId != answer.QuestionId)
                    throw new ArgumentException(
                        $"الاختيار رقم {answer.SelectedOptionId} لا يخص السؤال رقم {answer.QuestionId}",
                        nameof(dto));

                if (answer.SelectedOptionId != autoGradedByQuestion[answer.QuestionId])
                    confirmedMistakes.Add(answer);
            }

            // Counts stay counts; the score and the totals weigh each question by
            // its snapshot Points.
            var autoGradedCount = (short)autoGradedByQuestion.Count;
            var correctAnswers = (short)(autoGradedCount - confirmedMistakes.Count);
            var wrongQuestionIds = confirmedMistakes.Select(m => m.QuestionId).ToHashSet();

            var scoredQuestions = attemptQuestions
                .Select(aq => new ScoredQuestion(aq.QuestionId, aq.QuestionType, aq.Points))
                .ToList();

            // Decided BEFORE the transaction: it reads the Content module's levels
            // through another DbContext, which must not sit inside our transaction.
            PlacementDecision? placement = null;

            if (isPlacement)
                placement = await _placement.EvaluateAsync(
                    attemptQuestions
                        .Where(aq => QuestionTypes.IsAutoGraded(aq.QuestionType))
                        .ToDictionary(aq => aq.QuestionId, aq => aq.Points),
                    wrongQuestionIds,
                    _settings.EffectivePlacementPassPercentage,
                    cancellationToken);

            try
            {
                return await CommitSubmissionAsync(
                    attempt, confirmedMistakes, submittedEssays, scoredQuestions, wrongQuestionIds, autoGradedCount,
                    correctAnswers, placement, language, essayMaxPoints);
            }
            catch (DbUpdateException ex) when (ex.IsUniqueViolationOf("UQ_UserTopicStats_UserId_TopicId_Difficulty"))
            {
                // Two different attempts by the same child created the same
                // statistics row at the same instant. Nothing was saved and the
                // attempt is still InProgress, so a retry succeeds.
                _db.ChangeTracker.Clear();

                throw new ConflictException(
                    $"تعذّر تسليم المحاولة رقم {attemptId} بسبب طلب متزامن، برجاء إعادة المحاولة", ex);
            }
            catch (Exception ex) when (IsLostSubmissionRace(ex))
            {
                // Something else interfered: a concurrent submit of this attempt
                // (RowVersion / unique answer or placement rows / a deadlock), the
                // abandoned-attempt sweep (RowVersion), another attempt updating the
                // same statistics row (UserTopicStats.RowVersion), or another
                // placement attempt of this learner. Our transaction is already
                // rolled back, so look at what actually happened.
                _db.ChangeTracker.Clear();

                var status = await _db.QuizAttempts
                    .AsNoTracking()
                    .Where(a => a.Id == attemptId)
                    .Select(a => a.Status)
                    .FirstOrDefaultAsync(cancellationToken);

                if (status == QuizAttemptStatuses.Completed)
                    return null;

                if (status == QuizAttemptStatuses.Abandoned)
                    throw AttemptAbandoned(attemptId);

                if (ex is DbUpdateException placementClash
                    && placementClash.IsUniqueViolationOf("UQ_UserPlacements_UserId"))
                    throw new ConflictException(AlreadyPlacedMessage, ex);

                throw new ConflictException(
                    $"تعذّر تسليم المحاولة رقم {attemptId} بسبب طلب متزامن، برجاء إعادة المحاولة", ex);
            }
        }

        /// <summary>
        /// The one transaction of a submission. Runs with CancellationToken.None on
        /// purpose: once grading has started writing, a dropped connection must not
        /// cancel it half-way — the child's result is saved, and Flutter recovers it
        /// with GET /api/quiz-attempts/{id}/result.
        /// </summary>
        private async Task<GradedSubmission> CommitSubmissionAsync(
            QuizAttempt attempt,
            IReadOnlyList<QuizAttemptMistakeDto> confirmedMistakes,
            IReadOnlyList<QuizAttemptEssayAnswerDto> essays,
            IReadOnlyList<ScoredQuestion> scoredQuestions,
            IReadOnlySet<int> wrongQuestionIds,
            short autoGradedCount,
            short correctAnswers,
            PlacementDecision? placement,
            string language,
            IReadOnlyDictionary<int, byte> essayMaxPoints)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(CancellationToken.None);

            var recordedMistakes = confirmedMistakes
                .Select(m => new QuizAttemptMistake
                {
                    QuizAttemptId = attempt.Id,
                    QuestionId = m.QuestionId,
                    SelectedOptionId = m.SelectedOptionId
                })
                .ToList();

            _db.QuizAttemptMistakes.AddRange(recordedMistakes);

            // Essay answers are stored Pending and never enter ScorePercentage. The
            // AI evaluates them after the commit; the language is kept so feedback
            // comes back in it even when the background evaluator does the work.
            foreach (var essay in essays)
            {
                _db.QuizAttemptEssayAnswers.Add(new QuizAttemptEssayAnswer
                {
                    QuizAttemptId = attempt.Id,
                    QuestionId = essay.QuestionId,
                    AnswerText = essay.AnswerText.Trim(),
                    Status = EssayAnswerStatuses.Pending,
                    LanguageCode = language,
                    MaxPoints = essayMaxPoints[essay.QuestionId]
                });
            }

            // InProgress → Completed. RowVersion makes this UPDATE affect 0 rows if
            // anything changed the attempt since it was read, so this transition
            // can happen exactly once.
            attempt.QuestionsAnsweredCount = attempt.TotalQuestionsAtAttempt;
            attempt.CorrectAnswersCount = correctAnswers;
            attempt.ScorePercentage = AttemptScoring.ScorePercentage(scoredQuestions, wrongQuestionIds);
            attempt.Status = QuizAttemptStatuses.Completed;
            attempt.CompletedAt = _clock.UtcNow;

            // The placement is as critical as the score: same transaction.
            if (placement is not null)
            {
                _db.UserPlacements.Add(new UserPlacement
                {
                    UserId = attempt.UserId,
                    QuizAttemptId = attempt.Id,
                    PlacedLevelId = placement.PlacedLevel.Id,
                    ScorePercentage = attempt.ScorePercentage,
                    PassPercentage = (byte)placement.PassPercentage,
                    PlacedAt = attempt.CompletedAt.Value
                });
            }

            await _db.SaveChangesAsync(CancellationToken.None);

            await _userTopicStatService.UpdateAfterQuizAttemptAsync(attempt.Id, attempt.UserId, CancellationToken.None);

            await transaction.CommitAsync(CancellationToken.None);

            return new GradedSubmission(
                attempt.Id,
                attempt.QuizId,
                attempt.CompletedAt.Value,
                attempt.TotalQuestionsAtAttempt,
                autoGradedCount,
                (short)essays.Count,
                correctAnswers,
                attempt.ScorePercentage,
                // As committed: no essay has been evaluated yet, so all are Pending.
                AttemptScoring.CountPoints(
                    scoredQuestions,
                    wrongQuestionIds,
                    essays.Select(e => new EssayResultDto
                    {
                        QuestionId = e.QuestionId,
                        Status = EssayAnswerStatuses.Pending,
                        MaxPoints = essayMaxPoints[e.QuestionId]
                    })),
                recordedMistakes
                    .Select(m => new RecordedMistake(m.Id, m.QuestionId, m.SelectedOptionId))
                    .ToList(),
                placement is null
                    ? null
                    : PlacementEngine.ToResultDto(
                        placement, placement.PlacedLevel.Id, attempt.ScorePercentage, attempt.CompletedAt.Value));
        }

        /// <summary>
        /// Every way a concurrent request can make this submission lose: RowVersion
        /// (another submit or the sweep changed the attempt, or another attempt
        /// changed a shared statistics row), a unique answer or placement row the
        /// other request inserted first, or a deadlock victim (the server rolled
        /// us back). The caller re-reads the attempt to decide the answer.
        /// </summary>
        private static bool IsLostSubmissionRace(Exception exception) =>
            exception is DbUpdateConcurrencyException
            || exception.IsDeadlockVictim()
            || (exception is DbUpdateException update
                && (update.IsUniqueViolationOf("UQ_QuizAttemptMistakes_AttemptId_QuestionId")
                 || update.IsUniqueViolationOf("UQ_QuizAttemptEssayAnswers_AttemptId_QuestionId")
                 || update.IsUniqueViolationOf("UQ_UserPlacements_UserId")
                 || update.IsUniqueViolationOf("UQ_UserPlacements_QuizAttemptId")));

        /// <summary>
        /// Phase B, hints. Never throws. The result is already committed; every
        /// failure here — the AI, or even the database — degrades to "no hints",
        /// never to a failed submission. The AI call is bounded by the caller's
        /// <paramref name="aiToken"/> budget.
        /// </summary>
        private async Task<RetrySet> BuildRetryQuestionsWithHintsAsync(
            Guid userId,
            GradedSubmission graded,
            string language,
            CancellationToken aiToken)
        {
            if (graded.Mistakes.Count == 0)
                return RetrySet.None();

            List<LocalizedQuestionRow> rows;

            try
            {
                var questionIds = graded.Mistakes.Select(m => m.QuestionId).ToList();

                rows = await LocalizedQuestionQuery.Project(
                        _db.Questions.AsNoTracking().Where(q => questionIds.Contains(q.Id)), language)
                    .ToListAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Attempt {AttemptId} was saved, but its retry questions could not be loaded. "
                  + "The result is returned without them; the result endpoint can recover them.",
                    graded.AttemptId);

                return new RetrySet([], false, HintStatuses.Unavailable);
            }

            var hints = await TryGenerateHintsAsync(userId, graded, rows, language, aiToken);

            if (hints.Count > 0)
                await TryPersistHintsAsync(userId, graded, hints, language);

            return new RetrySet(
                BuildRetryQuestions(rows, hints),
                rows.Any(r => r.UsedFallback),
                HintStatuses.Resolve(graded.Mistakes.Count, hints.Count));
        }

        private async Task<IReadOnlyDictionary<int, string>> TryGenerateHintsAsync(
            Guid userId,
            GradedSubmission graded,
            IReadOnlyList<LocalizedQuestionRow> rows,
            string language,
            CancellationToken aiToken)
        {
            // Not configured is the normal state until an AI service exists — not
            // worth a warning with a stack trace on every submission.
            if (!_aiHintGenerator.IsConfigured)
                return NoHints;

            try
            {
                return await GenerateHintsAsync(userId, graded, rows, language, aiToken);
            }
            catch (OperationCanceledException ex) when (aiToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex,
                    "AI hint generation for attempt {AttemptId} exceeded {TimeoutSeconds}s. "
                  + "The saved result is returned without hints.",
                    graded.AttemptId, _settings.AiHintTimeout.TotalSeconds);
            }
            catch (Exception ex)
            {
                // Provider down or unreachable, HTTP error, malformed payload —
                // hints are optional, so all of them end here.
                _logger.LogWarning(ex,
                    "AI hint generation for attempt {AttemptId} failed. The saved result is returned without hints.",
                    graded.AttemptId);
            }

            return NoHints;
        }

        private async Task TryPersistHintsAsync(
            Guid userId,
            GradedSubmission graded,
            IReadOnlyDictionary<int, string> hints,
            string language)
        {
            try
            {
                await PersistHintsAsync(graded.AttemptId, graded.Mistakes, hints, language, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Hints that failed to save are still returned in this response.
                _db.ChangeTracker.Clear();
                _logger.LogError(ex, "Hints for attempt {AttemptId} were generated but could not be saved.", graded.AttemptId);
                return;
            }

            try
            {
                // Hints are saved after the statistics were committed, so re-derive
                // HintsUsedCount for this attempt's buckets. It is recomputed from
                // saved hints, never accumulated, so a miss self-heals next time.
                await _userTopicStatService.RefreshHintsUsedCountAsync(graded.AttemptId, userId, CancellationToken.None);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _db.ChangeTracker.Clear();
                _logger.LogWarning(ex,
                    "HintsUsedCount for attempt {AttemptId} was not refreshed: another submission updated the same "
                  + "statistics at the same time. The hints are saved; the count is recomputed on the next submission.",
                    graded.AttemptId);
            }
            catch (Exception ex)
            {
                _db.ChangeTracker.Clear();
                _logger.LogError(ex,
                    "HintsUsedCount for attempt {AttemptId} could not be refreshed. The hints are saved.", graded.AttemptId);
            }
        }

        /// <summary>
        /// Builds the v1 hint request (docs/AI_CONTRACT.md): for each wrong answer,
        /// the question, its type, every option, the child's choice and the correct
        /// option from the frozen answer key — never per-option isCorrect flags,
        /// never a user or attempt id. Then keeps only hints that are well-formed,
        /// short enough, and do not name the correct answer.
        /// </summary>
        private async Task<IReadOnlyDictionary<int, string>> GenerateHintsAsync(
            Guid userId,
            GradedSubmission graded,
            IReadOnlyList<LocalizedQuestionRow> rows,
            string language,
            CancellationToken cancellationToken)
        {
            var mistakes = graded.Mistakes;
            var questionIds = mistakes.Select(m => m.QuestionId).ToList();

            // The key and type this attempt was graded against, even if an admin has
            // edited the question since.
            var answerKeys = await _db.QuizAttemptQuestions
                .AsNoTracking()
                .Where(aq => aq.QuizAttemptId == graded.AttemptId && questionIds.Contains(aq.QuestionId))
                .Select(aq => new { aq.QuestionId, aq.CorrectOptionId, aq.QuestionType, aq.Difficulty })
                .ToDictionaryAsync(aq => aq.QuestionId, cancellationToken);

            var topics = await _aiRequests.LoadTopicNamesAsync(questionIds, language, cancellationToken);

            // Age only, and only when it is known — so a hint can be pitched at the
            // child's level. Nothing else about the learner is ever sent.
            var age = await _learners.GetAgeAsync(userId, cancellationToken);

            // Previous hints in the SAME language — an Arabic hint is not context
            // for an English one.
            // Through the hint's own (attempt, question) link: a Hint-button hint
            // has no mistake row, and going through the mistake would skip it.
            var previousHints = await _db.QuestionHints
                .AsNoTracking()
                .Where(h => questionIds.Contains(h.QuestionId)
                         && h.QuizAttemptQuestion.QuizAttempt.UserId == userId
                         && h.LanguageCode == language)
                .OrderBy(h => h.QuizAttemptId)
                .ThenBy(h => h.HintSequence)
                .Select(h => new { h.QuestionId, h.HintText })
                .ToListAsync(cancellationToken);

            var hintsByQuestion = previousHints
                .GroupBy(h => h.QuestionId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(h => h.HintText).ToList());

            var rowsById = rows.ToDictionary(r => r.QuestionId);
            var items = new List<HintRequestItem>(mistakes.Count);
            var imageBudget = _aiRequests.NewImageBudget();

            // What the child sees as the correct option, whether it is a TrueFalse,
            // and (for TrueFalse) the other option — for the leak check on the way back.
            var correctAnswerByQuestion = new Dictionary<int, (string? Text, bool IsBinary, string? Other)>(mistakes.Count);

            foreach (var m in mistakes)
            {
                if (!rowsById.TryGetValue(m.QuestionId, out var row)
                    || !answerKeys.TryGetValue(m.QuestionId, out var key)
                    || key.CorrectOptionId is not int correctOptionId)
                    continue;

                var selected = row.Options.FirstOrDefault(o => o.OptionId == m.SelectedOptionId);
                var correct = row.Options.FirstOrDefault(o => o.OptionId == correctOptionId);

                if (selected is null || correct is null)
                    continue;

                // Never ask about something the AI cannot interpret: an image-only
                // question or option without its description is excluded and
                // reported rather than sent empty.
                if (ResolveQuestionSemanticText(row.QuestionText, row.ImageDescription) is null)
                {
                    _logger.LogWarning(
                        "Skipping hint generation for question {QuestionId}: it has neither text nor an ImageDescription.",
                        m.QuestionId);
                    continue;
                }

                if (ResolveOptionSemanticText(selected.OptionText, selected.ImageDescription) is null
                    || ResolveOptionSemanticText(correct.OptionText, correct.ImageDescription) is null)
                {
                    _logger.LogWarning(
                        "Skipping hint generation for question {QuestionId}: the selected or the correct option has no text "
                      + "and no ImageDescription, so the AI cannot interpret it. An admin must supply ImageDescription.",
                        m.QuestionId);
                    continue;
                }

                var options = new List<AiOption>(row.Options.Count);

                foreach (var option in row.Options)
                    options.Add(new AiOption
                    {
                        OptionId = option.OptionId,
                        Text = AiRequestBuilder.Clean(option.OptionText),
                        Image = await _aiRequests.BuildImageAsync(
                            option.ImageUrl, option.ImageDescription, imageBudget, cancellationToken)
                    });

                items.Add(new HintRequestItem
                {
                    QuestionId = m.QuestionId,
                    QuestionType = key.QuestionType,
                    Difficulty = key.Difficulty,
                    Topic = topics.GetValueOrDefault(m.QuestionId),
                    Question = new AiQuestion
                    {
                        Text = AiRequestBuilder.Clean(row.QuestionText),
                        Image = await _aiRequests.BuildImageAsync(
                            row.ImageUrl, row.ImageDescription, imageBudget, cancellationToken)
                    },
                    Options = options,
                    StudentAnswer = new HintStudentAnswer { SelectedOptionId = m.SelectedOptionId },
                    Reference = new HintReference { CorrectOptionId = correctOptionId },
                    PreviousHints = hintsByQuestion.TryGetValue(m.QuestionId, out var history)
                        ? history
                        : []
                });

                correctAnswerByQuestion[m.QuestionId] = (
                    correct.OptionText,
                    key.QuestionType == QuestionTypes.TrueFalse,
                    row.Options.FirstOrDefault(o => o.OptionId != correctOptionId)?.OptionText);
            }

            if (items.Count == 0)
                return NoHints;

            var response = await _aiHintGenerator.GenerateHintsAsync(
                new GenerateHintsRequest
                {
                    RequestId = Guid.NewGuid(),
                    Language = language,
                    LearnerContext = age is int years ? new LearnerContext { Age = years } : null,
                    Items = items
                },
                cancellationToken);

            // Only hints for questions that were actually ASKED, one each. A
            // duplicate, a blank, an over-long hint, or one that names the correct
            // answer is dropped — that question just has no hint.
            var results = (response?.Results ?? Array.Empty<HintResult>())
                .Where(r => r is not null && correctAnswerByQuestion.ContainsKey(r.QuestionId))
                .GroupBy(r => r.QuestionId)
                .Where(g => g.Count() == 1)
                .Select(g => g.Single());

            var accepted = new Dictionary<int, string>();

            foreach (var result in results)
            {
                if (result.Status is not null
                    && !string.Equals(result.Status, AiResultStatuses.Ok, StringComparison.OrdinalIgnoreCase))
                    continue;

                var hint = result.Hint?.Trim();

                if (string.IsNullOrEmpty(hint))
                    continue;

                if (hint.Length > _settings.EffectiveMaxHintLength)
                {
                    _logger.LogWarning(
                        "Dropped the AI hint for question {QuestionId}: {Length} characters exceeds the {Max} limit.",
                        result.QuestionId, hint.Length, _settings.EffectiveMaxHintLength);
                    continue;
                }

                var (correctText, isBinary, otherText) = correctAnswerByQuestion[result.QuestionId];

                if (HintSafety.RevealsAnswer(hint, correctText, isBinary, otherText)
                    || (!isBinary && HintSafety.IsTooSimilar(
                        hint, correctText, _settings.EffectiveHintSimilarityThreshold)))
                {
                    _logger.LogWarning(
                        "Dropped the AI hint for question {QuestionId}: it names the correct answer.", result.QuestionId);
                    continue;
                }

                accepted[result.QuestionId] = hint;
            }

            if (accepted.Count < items.Count)
                _logger.LogWarning(
                    "The AI returned usable hints for {Accepted} of {Asked} wrong questions; "
                  + "the rest are returned without a hint.",
                    accepted.Count, items.Count);

            return accepted;
        }

        /// <summary>
        /// Phase B, essays. Never throws: out of budget, AI down or a database
        /// hiccup all leave the essays Pending for the background evaluator.
        /// </summary>
        private async Task TryEvaluateEssaysAsync(long attemptId, CancellationToken aiToken)
        {
            // Hints used the whole budget: nothing to try, and nothing worth logging.
            if (aiToken.IsCancellationRequested)
                return;

            try
            {
                await _essays.EvaluateAttemptAsync(attemptId, aiToken);
            }
            catch (Exception ex)
            {
                _db.ChangeTracker.Clear();
                _logger.LogWarning(ex,
                    "Inline essay evaluation for attempt {AttemptId} did not complete; the background evaluator will retry.",
                    attemptId);
            }
        }

        private async Task<SavedGrading?> TryLoadSavedGradingAsync(long attemptId)
        {
            try
            {
                return await LoadSavedGradingAsync(attemptId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Attempt {AttemptId} was saved, but its essay results and points could not be loaded; "
                  + "the result endpoint can recover them.",
                    attemptId);
                return null;
            }
        }

        /// <summary>
        /// The one read every result of a submitted attempt is built from — the
        /// submit response, its replay and GET .../result — so they can never
        /// disagree. The frozen snapshot gives each question's type and Points;
        /// only WRONG answers are stored, so a snapshot question with no mistake
        /// row was answered correctly; the essay rows give the AI's grades.
        /// </summary>
        private async Task<SavedGrading> LoadSavedGradingAsync(long attemptId, CancellationToken cancellationToken)
        {
            var questions = await _db.QuizAttemptQuestions
                .AsNoTracking()
                .Where(aq => aq.QuizAttemptId == attemptId)
                .Select(aq => new ScoredQuestion(aq.QuestionId, aq.QuestionType, aq.Points))
                .ToListAsync(cancellationToken);

            var wrongQuestionIds = await _db.QuizAttemptMistakes
                .AsNoTracking()
                .Where(m => m.QuizAttemptId == attemptId)
                .Select(m => m.QuestionId)
                .ToListAsync(cancellationToken);

            var essays = await LoadEssayResultsAsync(attemptId, cancellationToken);

            return new SavedGrading(
                essays,
                wrongQuestionIds,
                (short)questions.Count(q => QuestionTypes.IsAutoGraded(q.QuestionType)),
                AttemptScoring.CountPoints(questions, wrongQuestionIds.ToHashSet(), essays));
        }

        /// <summary>
        /// Essays are graded by the AI only. Pending = not finished yet; Graded and
        /// NotGraded are final. Points and feedback are shown only for Graded, and
        /// MaxPoints is the question's Points frozen at attempt start.
        /// </summary>
        private Task<List<EssayResultDto>> LoadEssayResultsAsync(long attemptId, CancellationToken cancellationToken) =>
            _db.QuizAttemptEssayAnswers
                .AsNoTracking()
                .Where(e => e.QuizAttemptId == attemptId)
                .Join(
                    _db.QuizAttemptQuestions,
                    e => new { e.QuizAttemptId, e.QuestionId },
                    aq => new { aq.QuizAttemptId, aq.QuestionId },
                    (e, aq) => new { Essay = e, aq.Points })
                .OrderBy(x => x.Essay.Question.DisplayOrder)
                .Select(x => new EssayResultDto
                {
                    QuestionId = x.Essay.QuestionId,
                    Status = x.Essay.Status,
                    AwardedPoints = x.Essay.Status == EssayAnswerStatuses.Graded ? x.Essay.AwardedPoints : null,
                    MaxPoints = x.Points,
                    Feedback = x.Essay.Status == EssayAnswerStatuses.Graded ? x.Essay.Feedback : null
                })
                .ToListAsync(cancellationToken);

        /// <summary>
        /// A question skipped for hint generation still belongs in the retry set —
        /// it just carries no hint, so CurrentHint stays null.
        /// </summary>
        private static List<QuizQuestionForAttemptDto> BuildRetryQuestions(
            IReadOnlyList<LocalizedQuestionRow> rows,
            IReadOnlyDictionary<int, string> hintTextByQuestion) =>
            rows
                .OrderBy(r => r.DisplayOrder)
                .Select(r =>
                {
                    var dto = r.ToDto();
                    dto.CurrentHint = hintTextByQuestion.TryGetValue(r.QuestionId, out var hint)
                        ? hint
                        : null;
                    return dto;
                })
                .ToList();

        /// <summary>See <see cref="AiRequestBuilder.QuestionSemanticText"/>.</summary>
        private static string? ResolveQuestionSemanticText(string? questionText, string? imageDescription) =>
            AiRequestBuilder.QuestionSemanticText(questionText, imageDescription);

        /// <summary>See <see cref="AiRequestBuilder.OptionSemanticText"/>.</summary>
        private static string? ResolveOptionSemanticText(string? optionText, string? imageDescription) =>
            AiRequestBuilder.OptionSemanticText(optionText, imageDescription);

        /// <summary>
        /// Saves one hint per mistake that actually received one. A mistake the AI
        /// skipped or answered badly simply gets no row.
        /// </summary>
        private async Task PersistHintsAsync(
            long attemptId,
            IReadOnlyList<RecordedMistake> mistakes,
            IReadOnlyDictionary<int, string> hintTextByQuestion,
            string language,
            CancellationToken cancellationToken)
        {
            var hinted = mistakes
                .Where(m => hintTextByQuestion.ContainsKey(m.QuestionId))
                .ToList();

            if (hinted.Count == 0)
                return;

            var questionIds = hinted.Select(m => m.QuestionId).ToList();

            // Sequence numbering is per (attempt, question, language) — mirrors
            // UQ_QuestionHints_AttemptId_QuestionId_Language_Sequence, and continues
            // after any Hint-button hints the child already took on that question.
            // Projected to (key, max) so the aggregate runs in SQL.
            var nextSequences = await _db.QuestionHints
                .AsNoTracking()
                .Where(h => h.QuizAttemptId == attemptId
                         && questionIds.Contains(h.QuestionId)
                         && h.LanguageCode == language)
                .GroupBy(h => h.QuestionId)
                .Select(g => new { QuestionId = g.Key, Next = g.Max(h => h.HintSequence) + 1 })
                .ToDictionaryAsync(x => x.QuestionId, x => (byte)x.Next, cancellationToken);

            _db.QuestionHints.AddRange(hinted.Select(m => new QuestionHint
            {
                QuizAttemptId = attemptId,
                QuestionId = m.QuestionId,
                QuizAttemptMistakeId = m.Id,
                HintText = hintTextByQuestion[m.QuestionId],
                LanguageCode = language,
                // Not an escalation level: this hint follows a submission.
                AttemptNumber = null,
                HintSequence = nextSequences.TryGetValue(m.QuestionId, out var sequence) ? sequence : (byte)1
            }));

            await _db.SaveChangesAsync(cancellationToken);
        }

        /// <summary>
        /// The saved result of a submitted attempt. Works whether or not the
        /// original submit response ever reached the client, and is also what a
        /// repeated submit replays. Essay grades that arrived later appear here.
        /// </summary>
        public async Task<QuizAttemptResultDto> GetResultAsync(
            long attemptId,
            Guid userId,
            string? language = null,
            CancellationToken cancellationToken = default)
        {
            var resolvedLanguage = ContentLanguages.Normalize(language);

            // Ownership comes from the token's sub claim, never from the request, and
            // is part of the filter: another user's attempt is reported exactly like
            // one that does not exist, so a 404 never confirms that an id is real.
            var attempt = await _db.QuizAttempts
                .AsNoTracking()
                .Where(a => a.Id == attemptId && a.UserId == userId)
                .Select(a => new
                {
                    a.Id,
                    a.QuizId,
                    a.Status,
                    a.StartedAt,
                    a.CompletedAt,
                    a.TotalQuestionsAtAttempt,
                    a.CorrectAnswersCount,
                    a.ScorePercentage,
                    a.Quiz.QuizType
                })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException($"المحاولة رقم {attemptId} غير موجودة");

            if (attempt.Status == QuizAttemptStatuses.Abandoned
                || (attempt.Status == QuizAttemptStatuses.InProgress && IsExpired(attempt.StartedAt)))
                throw AttemptAbandoned(attemptId);

            if (attempt.Status != QuizAttemptStatuses.Completed)
                throw new ConflictException(
                    $"المحاولة رقم {attemptId} لم يتم تسليمها بعد، برجاء إرسال الإجابات");

            var saved = await LoadSavedGradingAsync(attemptId, cancellationToken);

            PlacementResultDto? placement = null;
            RetrySet retry;

            if (attempt.QuizType == QuizTypes.Placement)
            {
                placement = await LoadPlacementResultAsync(attemptId, cancellationToken);
                retry = RetrySet.None();
            }
            else
            {
                retry = await LoadSavedRetryQuestionsAsync(
                    attemptId, saved.WrongQuestionIds, resolvedLanguage, cancellationToken);
            }

            return new QuizAttemptResultDto
            {
                AttemptId = attempt.Id,
                QuizId = attempt.QuizId,
                CompletedAt = attempt.CompletedAt,
                TotalQuestions = attempt.TotalQuestionsAtAttempt,
                AutoGradedQuestions = saved.AutoGradedQuestions,
                PendingEssayQuestions = saved.PendingEssayQuestions,
                EssayResults = saved.EssayResults,
                CorrectAnswers = attempt.CorrectAnswersCount,
                // Not the persisted WrongAnswersCount: that computed column is
                // answered − correct, and "answered" includes essays.
                WrongAnswers = (short)saved.WrongQuestionIds.Count,
                // The committed score, never recomputed: it is final at submit.
                ScorePercentage = attempt.ScorePercentage,
                TotalPoints = saved.Points.TotalPoints,
                EarnedPoints = saved.Points.EarnedPoints,
                PendingPoints = saved.Points.PendingPoints,
                Language = resolvedLanguage,
                LanguageFallbackApplied = retry.UsedFallback,
                HintsStatus = retry.HintsStatus,
                RetryQuestions = retry.Questions,
                Placement = placement
            };
        }

        /// <summary>
        /// The result of the user's last submitted attempt. Only a Completed attempt
        /// has a result, so "latest" is the latest CompletedAt — an attempt started
        /// later and still in progress, or abandoned, does not hide it.
        /// </summary>
        public async Task<QuizAttemptResultDto> GetLatestResultAsync(
            Guid userId,
            string? language = null,
            CancellationToken cancellationToken = default)
        {
            // The owner comes from the token, never from the request.
            var latestAttemptId = await _db.QuizAttempts
                .AsNoTracking()
                .Where(a => a.UserId == userId && a.Status == QuizAttemptStatuses.Completed)
                .OrderByDescending(a => a.CompletedAt)
                .ThenByDescending(a => a.Id)
                .Select(a => (long?)a.Id)
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException("لا توجد محاولات مكتملة بعد، أكمل اختبارًا لتظهر نتيجتك هنا");

            // A Completed attempt never changes status again, so the checks inside
            // GetResultAsync always pass; reusing it keeps this result identical to
            // GET /api/quiz-attempts/{attemptId}/result.
            return await GetResultAsync(latestAttemptId, userId, language, cancellationToken);
        }

        private async Task<PlacementResultDto?> LoadPlacementResultAsync(
            long attemptId,
            CancellationToken cancellationToken)
        {
            var stored = await _db.UserPlacements
                .AsNoTracking()
                .Where(p => p.QuizAttemptId == attemptId)
                .Select(p => new { p.PlacedLevelId, p.ScorePercentage, p.PassPercentage, p.PlacedAt })
                .FirstOrDefaultAsync(cancellationToken);

            return stored is null
                ? null
                : await _placement.DescribeAsync(
                    attemptId,
                    stored.PlacedLevelId,
                    stored.ScorePercentage,
                    stored.PassPercentage,
                    stored.PlacedAt,
                    cancellationToken);
        }

        private async Task<RetrySet> LoadSavedRetryQuestionsAsync(
            long attemptId,
            List<int> wrongQuestionIds,
            string language,
            CancellationToken cancellationToken)
        {
            if (wrongQuestionIds.Count == 0)
                return RetrySet.None();

            var rows = await LocalizedQuestionQuery.Project(
                    _db.Questions.AsNoTracking().Where(q => wrongQuestionIds.Contains(q.Id)), language)
                .ToListAsync(cancellationToken);

            var latestHints = await GetLatestHintPerQuestionAsync(attemptId, language, cancellationToken);

            return new RetrySet(
                BuildRetryQuestions(rows, latestHints),
                rows.Any(r => r.UsedFallback),
                HintStatuses.Resolve(wrongQuestionIds.Count, wrongQuestionIds.Count(latestHints.ContainsKey)));
        }

        /// <summary>
        /// Moves every attempt still InProgress past the allowed window to
        /// Abandoned, database-side and in batches — nothing is loaded into memory.
        /// Safe to run repeatedly and on several instances at once: the WHERE only
        /// ever matches InProgress rows, so Completed attempts are never touched and
        /// a second run finds nothing to do.
        /// </summary>
        public async Task<int> AbandonExpiredAttemptsAsync(CancellationToken cancellationToken = default)
        {
            var cutoff = AbandonCutoff;
            var total = 0;
            int affected;

            do
            {
                affected = await _db.QuizAttempts
                    .Where(a => a.Status == QuizAttemptStatuses.InProgress && a.StartedAt <= cutoff)
                    .Take(AbandonSweepBatchSize)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(a => a.Status, QuizAttemptStatuses.Abandoned),
                        cancellationToken);

                total += affected;
            }
            while (affected == AbandonSweepBatchSize);

            return total;
        }

        /// <summary>See <see cref="AssessmentSettings.AbandonCutoff"/>.</summary>
        private DateTime AbandonCutoff => _settings.AbandonCutoff(_clock.UtcNow);

        private bool IsExpired(DateTime startedAt) => startedAt <= AbandonCutoff;

        private Task<int> MarkAbandonedAsync(long attemptId, CancellationToken cancellationToken) =>
            _db.QuizAttempts
                .Where(a => a.Id == attemptId && a.Status == QuizAttemptStatuses.InProgress)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(a => a.Status, QuizAttemptStatuses.Abandoned),
                    cancellationToken);

        private static GoneException AttemptAbandoned(long attemptId) =>
            new($"المحاولة رقم {attemptId} انتهت صلاحيتها قبل تسليمها، برجاء بدء محاولة جديدة");

        public async Task<QuizAttemptResponseDto> GetByIdAsync(
            long attemptId,
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
                .Select(a => new { a.Id, a.QuizId, a.StartedAt, a.Quiz.QuizType })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException($"المحاولة رقم {attemptId} غير موجودة");

            // The weight each question carries in THIS attempt, frozen at start.
            var snapshotPoints = await _db.QuizAttemptQuestions
                .AsNoTracking()
                .Where(aq => aq.QuizAttemptId == attemptId)
                .Select(aq => new { aq.QuestionId, aq.Points })
                .ToDictionaryAsync(aq => aq.QuestionId, aq => aq.Points, cancellationToken);

            List<LocalizedQuestionRow> rows;

            if (attempt.QuizType == QuizTypes.Placement)
            {
                // Questions from several quizzes: rebuild the serving order (level,
                // then DisplayOrder) and number it 1..n, exactly as Start served it.
                rows = await LoadRowsInOrderAsync(
                    await _placement.SortForServingAsync(snapshotPoints.Keys.ToList(), cancellationToken),
                    resolvedLanguage,
                    cancellationToken);

                NumberInAttemptOrder(rows);
            }
            else
            {
                rows = await LocalizedQuestionQuery.Project(
                        _db.QuizAttemptQuestions
                            .AsNoTracking()
                            .Where(aq => aq.QuizAttemptId == attemptId)
                            .Select(aq => aq.Question),
                        resolvedLanguage)
                    .ToListAsync(cancellationToken);
            }

            var latestHints = await GetLatestHintPerQuestionAsync(attemptId, resolvedLanguage, cancellationToken);

            foreach (var row in rows)
            {
                if (latestHints.TryGetValue(row.QuestionId, out var hintText))
                    row.CurrentHint = hintText;

                // Not the live Points: an admin re-weighting the question mid-attempt
                // must not change what the child is shown it is worth.
                if (snapshotPoints.TryGetValue(row.QuestionId, out var points))
                    row.Points = points;
            }

            return new QuizAttemptResponseDto
            {
                AttemptId = attempt.Id,
                QuizId = attempt.QuizId,
                StartedAt = attempt.StartedAt,
                Language = resolvedLanguage,
                LanguageFallbackApplied = rows.Any(r => r.UsedFallback),
                Questions = rows.Select(r => r.ToDto()).ToList()
            };
        }

        /// <summary>
        /// Structural checks on the MCQ/TF answers: no nulls, no duplicates, only
        /// questions of this attempt. Completeness is checked separately, across
        /// every question type, by <see cref="EnsureEveryQuestionAnswered"/>.
        /// </summary>
        private static List<QuizAttemptMistakeDto> ValidateSubmittedAnswers(
            SubmitQuizAttemptDto dto,
            HashSet<int> autoGradedQuestionIds)
        {
            var answers = dto.Mistakes ?? new List<QuizAttemptMistakeDto>();
            var seenQuestionIds = new HashSet<int>(answers.Count);

            foreach (var answer in answers)
            {
                if (answer is null)
                    throw new ArgumentException("قائمة الإجابات تحتوي على عنصر فارغ", nameof(dto));

                if (!seenQuestionIds.Add(answer.QuestionId))
                    throw new ArgumentException(
                        $"السؤال رقم {answer.QuestionId} مكرر في قائمة الإجابات", nameof(dto));

                if (!autoGradedQuestionIds.Contains(answer.QuestionId))
                    throw new ArgumentException(
                        $"السؤال رقم {answer.QuestionId} لا يخص هذه المحاولة أو لا يُصحّح تلقائيًا", nameof(dto));
            }

            return answers.ToList();
        }

        private static List<QuizAttemptEssayAnswerDto> ValidateSubmittedEssays(
            SubmitQuizAttemptDto dto,
            HashSet<int> essayQuestionIds,
            int maxAnswerLength)
        {
            var essays = dto.EssayAnswers ?? new List<QuizAttemptEssayAnswerDto>();
            var seenQuestionIds = new HashSet<int>(essays.Count);

            foreach (var essay in essays)
            {
                if (essay is null)
                    throw new ArgumentException("قائمة الإجابات المقالية تحتوي على عنصر فارغ", nameof(dto));

                if (!seenQuestionIds.Add(essay.QuestionId))
                    throw new ArgumentException(
                        $"السؤال رقم {essay.QuestionId} مكرر في قائمة الإجابات المقالية", nameof(dto));

                if (!essayQuestionIds.Contains(essay.QuestionId))
                    throw new ArgumentException(
                        $"السؤال رقم {essay.QuestionId} ليس سؤالًا مقاليًا في هذه المحاولة", nameof(dto));

                // A blank essay is an unanswered question, not an answer.
                if (string.IsNullOrWhiteSpace(essay.AnswerText))
                    throw new ArgumentException(
                        $"إجابة السؤال رقم {essay.QuestionId} فارغة", nameof(dto));

                // The column is nvarchar(max); this is the only bound on it.
                if (essay.AnswerText.Trim().Length > maxAnswerLength)
                    throw new ArgumentException(
                        $"إجابة السؤال رقم {essay.QuestionId} تتجاوز {maxAnswerLength} حرفًا", nameof(dto));
            }

            return essays.ToList();
        }

        /// <summary>
        /// Every question of the attempt — MultipleChoice, TrueFalse and Essay —
        /// must be answered. An omitted answer used to be counted as correct (an
        /// empty submission scored 100% and could place a child at the top level)
        /// and an omitted essay was silently dropped. The 400 lists every
        /// unanswered question so the app can point the child at them.
        /// </summary>
        private static void EnsureEveryQuestionAnswered(
            IEnumerable<int> attemptQuestionIds,
            IReadOnlySet<int> answeredQuestionIds)
        {
            var unanswered = attemptQuestionIds
                .Where(id => !answeredQuestionIds.Contains(id))
                .OrderBy(id => id)
                .ToList();

            if (unanswered.Count > 0)
                throw new ArgumentException(
                    $"يجب الإجابة على كل أسئلة الاختبار؛ الأسئلة بدون إجابة: {string.Join(", ", unanswered)}",
                    "dto");
        }

        /// <summary>
        /// Latest hint per question in the requested language. When a question has
        /// no hint in that language (the child switched language mid-chain), the
        /// latest hint in any language is returned rather than nothing.
        /// </summary>
        private async Task<Dictionary<int, string>> GetLatestHintPerQuestionAsync(
            long quizAttemptId,
            string language,
            CancellationToken cancellationToken)
        {
            // Keyed on the hint's own attempt + question, so a Hint-button hint
            // taken mid-attempt shows up here too.
            var hints = await _db.QuestionHints
                .AsNoTracking()
                .Where(h => h.QuizAttemptId == quizAttemptId)
                .Select(h => new
                {
                    h.QuestionId,
                    h.HintSequence,
                    h.HintText,
                    h.LanguageCode
                })
                .ToListAsync(cancellationToken);

            return hints
                .GroupBy(h => h.QuestionId)
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        var inLanguage = g.Where(h => h.LanguageCode == language).ToList();
                        var pool = inLanguage.Count > 0 ? inLanguage : g.ToList();
                        return pool.OrderByDescending(h => h.HintSequence).First().HintText;
                    });
        }

        /// <summary>What Phase A committed — everything Phase B needs, as plain values.</summary>
        private sealed record GradedSubmission(
            long AttemptId,
            int QuizId,
            DateTime CompletedAt,
            short TotalQuestions,
            short AutoGradedQuestions,
            short EssayCount,
            short CorrectAnswers,
            decimal ScorePercentage,
            AttemptPoints Points,
            IReadOnlyList<RecordedMistake> Mistakes,
            PlacementResultDto? Placement);

        private sealed record RecordedMistake(long Id, int QuestionId, int SelectedOptionId);

        /// <summary>What <see cref="LoadSavedGradingAsync"/> read back for a submitted attempt.</summary>
        private sealed record SavedGrading(
            List<EssayResultDto> EssayResults,
            List<int> WrongQuestionIds,
            short AutoGradedQuestions,
            AttemptPoints Points)
        {
            public short PendingEssayQuestions =>
                (short)EssayResults.Count(e => e.Status == EssayAnswerStatuses.Pending);
        }

        private sealed record RetrySet(
            List<QuizQuestionForAttemptDto> Questions,
            bool UsedFallback,
            string HintsStatus)
        {
            public static RetrySet None() => new([], false, HintStatuses.NotRequired);
        }
    }

    /// <summary>One question of an attempt as frozen at attempt start, for scoring.</summary>
    public sealed record ScoredQuestion(int QuestionId, string QuestionType, byte Points);

    /// <summary>The points of an attempt. See <see cref="QuizAttemptResultDto"/>.</summary>
    public sealed record AttemptPoints(int TotalPoints, int EarnedPoints, int PendingPoints);

    /// <summary>
    /// How an attempt's points are counted. Every question weighs its snapshot
    /// Points (1 unless the admin set another value). Pure, so the commit, the
    /// submit response, its replay and GET .../result all count the same way.
    ///
    /// Only wrong MultipleChoice/TrueFalse answers are stored, so "correct" is
    /// always "in the snapshot, auto-graded, and not among the mistakes".
    /// Sums are int: TINYINT Points summed over a quiz overflow a byte.
    /// </summary>
    public static class AttemptScoring
    {
        /// <summary>
        /// Points of the correct MultipleChoice/TrueFalse answers ÷ Points of all
        /// MultipleChoice/TrueFalse questions × 100, 2dp, midpoint away from zero;
        /// 0 when there are none. Essays never count: this is committed at submit,
        /// the AI grades essays afterwards, and a committed score must never change.
        /// </summary>
        public static decimal ScorePercentage(
            IEnumerable<ScoredQuestion> questions,
            IReadOnlySet<int> wrongQuestionIds)
        {
            var total = 0;
            var earned = 0;

            foreach (var question in questions.Where(q => QuestionTypes.IsAutoGraded(q.QuestionType)))
            {
                total += question.Points;

                if (!wrongQuestionIds.Contains(question.QuestionId))
                    earned += question.Points;
            }

            return total <= 0
                ? 0m
                : Math.Round(earned * 100m / total, 2, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// TotalPoints: every question, essays included. EarnedPoints: the correct
        /// MultipleChoice/TrueFalse answers plus the AwardedPoints of Graded essays.
        /// PendingPoints: the MaxPoints of essays still Pending. A NotGraded essay
        /// is in the total only — it earns nothing and is no longer pending.
        /// </summary>
        public static AttemptPoints CountPoints(
            IEnumerable<ScoredQuestion> questions,
            IReadOnlySet<int> wrongQuestionIds,
            IEnumerable<EssayResultDto> essays)
        {
            var total = 0;
            var earned = 0;
            var pending = 0;

            foreach (var question in questions)
            {
                total += question.Points;

                if (QuestionTypes.IsAutoGraded(question.QuestionType)
                    && !wrongQuestionIds.Contains(question.QuestionId))
                    earned += question.Points;
            }

            foreach (var essay in essays)
            {
                if (essay.Status == EssayAnswerStatuses.Graded)
                    earned += essay.AwardedPoints ?? 0;
                else if (essay.Status == EssayAnswerStatuses.Pending)
                    pending += essay.MaxPoints;
            }

            return new AttemptPoints(total, earned, pending);
        }
    }
}
