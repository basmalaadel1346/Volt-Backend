using AssessmentBL.DTOs.Placement;
using AssessmentBL.Services.Constants;
using AssessmentDA.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Common.Abstractions;
using Shared.Common.Exceptions;
using Shared.Content;

namespace AssessmentBL.Services
{
    /// <summary>
    /// The rules of the first-run placement test, shared by QuizAttemptService
    /// (which serves and grades it) and PlacementService (which reports on it).
    ///
    /// The placement quiz owns no questions. It samples each level's active
    /// LevelAssessment quiz — the content that already defines what a child at
    /// that level must know — easiest level first, and places the child at the
    /// first level they have not shown they master.
    /// </summary>
    public sealed class PlacementEngine
    {
        private readonly AssessmentDbContext _db;
        private readonly ILevelCatalog _levels;
        private readonly IDateTimeProvider _clock;
        private readonly AssessmentSettings _settings;
        private readonly ILogger<PlacementEngine> _logger;

        public PlacementEngine(
            AssessmentDbContext db,
            ILevelCatalog levels,
            IDateTimeProvider clock,
            IOptions<AssessmentSettings> settings,
            ILogger<PlacementEngine> logger)
        {
            _db = db;
            _levels = levels;
            _clock = clock;
            _settings = settings.Value;
            _logger = logger;
        }

        /// <summary>The active placement quiz. The DB allows at most one; newest wins regardless.</summary>
        public Task<int?> FindActivePlacementQuizIdAsync(CancellationToken cancellationToken = default) =>
            _db.Quizzes
                .AsNoTracking()
                .Where(q => q.QuizType == QuizTypes.Placement && q.IsActive)
                .OrderByDescending(q => q.Id)
                .Select(q => (int?)q.Id)
                .FirstOrDefaultAsync(cancellationToken);

        /// <summary>An open, not-yet-expired placement attempt of this user, if any.</summary>
        public Task<long?> FindResumableAttemptIdAsync(
            Guid userId,
            int placementQuizId,
            CancellationToken cancellationToken = default)
        {
            var cutoff = _settings.AbandonCutoff(_clock.UtcNow);

            return _db.QuizAttempts
                .AsNoTracking()
                .Where(a => a.UserId == userId
                         && a.QuizId == placementQuizId
                         && a.Status == QuizAttemptStatuses.InProgress
                         && a.StartedAt > cutoff)
                .OrderByDescending(a => a.Id)
                .Select(a => (long?)a.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        /// <summary>
        /// The placement question set, in the order it is served: levels in
        /// learning order, and within a level up to
        /// <see cref="AssessmentSettings.PlacementQuestionsPerLevel"/> questions of
        /// that level's newest active LevelAssessment quiz, in its DisplayOrder.
        /// Empty when there is nothing to assess.
        /// </summary>
        public async Task<List<int>> SelectQuestionIdsAsync(CancellationToken cancellationToken = default)
        {
            var levels = await _levels.GetLevelsInOrderAsync(cancellationToken);
            if (levels.Count == 0)
                return [];

            var levelIds = levels.Select(l => l.Id).ToList();

            var assessments = await _db.Quizzes
                .AsNoTracking()
                .Where(q => q.QuizType == QuizTypes.LevelAssessment
                         && q.IsActive
                         && q.LevelId != null
                         && levelIds.Contains(q.LevelId.Value))
                .Select(q => new { q.Id, q.LevelId })
                .ToListAsync(cancellationToken);

            // Newest active assessment per level — the same tie-break the lesson lookup uses.
            var quizIdByLevel = assessments
                .GroupBy(q => q.LevelId!.Value)
                .ToDictionary(g => g.Key, g => g.Max(q => q.Id));

            if (quizIdByLevel.Count == 0)
                return [];

            var quizIds = quizIdByLevel.Values.ToList();

            // Auto-graded only (an Essay cannot be scored on submit) and answerable
            // only (a question with no correct option would fail the attempt snapshot).
            var candidates = await _db.Questions
                .AsNoTracking()
                .Where(q => quizIds.Contains(q.QuizId)
                         && q.IsActive
                         && q.QuestionType != QuestionTypes.Essay
                         && q.QuestionOptions.Any(o => o.IsCorrect))
                .OrderBy(q => q.DisplayOrder)
                .ThenBy(q => q.Id)
                .Select(q => new { q.Id, q.QuizId })
                .ToListAsync(cancellationToken);

            var perLevel = _settings.EffectivePlacementQuestionsPerLevel;

            // GroupBy keeps the DisplayOrder of the query within each group.
            var idsByQuiz = candidates
                .GroupBy(c => c.QuizId)
                .ToDictionary(g => g.Key, g => g.Take(perLevel).Select(c => c.Id).ToList());

            var selected = new List<int>();

            foreach (var level in levels)
            {
                if (quizIdByLevel.TryGetValue(level.Id, out var quizId)
                    && idsByQuiz.TryGetValue(quizId, out var ids))
                    selected.AddRange(ids);
            }

            return selected;
        }

        /// <summary>
        /// Puts an attempt's placement questions back in serving order — level
        /// Order, then the question's DisplayOrder. Recomputed instead of trusting
        /// the snapshot rows' identity values, whose order SQL Server does not
        /// guarantee for a batched insert.
        /// </summary>
        public async Task<List<int>> SortForServingAsync(
            IReadOnlyCollection<int> questionIds,
            CancellationToken cancellationToken = default)
        {
            var levels = await _levels.GetLevelsInOrderAsync(cancellationToken);

            var rankOfLevel = levels
                .Select((level, rank) => (level.Id, rank))
                .ToDictionary(x => x.Id, x => x.rank);

            var ids = questionIds.ToList();

            var rows = await _db.Questions
                .AsNoTracking()
                .Where(q => ids.Contains(q.Id))
                .Select(q => new { q.Id, q.DisplayOrder, q.Quiz.LevelId })
                .ToListAsync(cancellationToken);

            return rows
                .OrderBy(r => r.LevelId is int levelId && rankOfLevel.TryGetValue(levelId, out var rank) ? rank : int.MaxValue)
                .ThenBy(r => r.DisplayOrder)
                .ThenBy(r => r.Id)
                .Select(r => r.Id)
                .ToList();
        }

        /// <summary>
        /// Grades a finished placement against the current level order.
        /// <paramref name="pointsOfAskedQuestion"/> maps every auto-graded question
        /// of the attempt to its Points frozen at attempt start.
        /// </summary>
        public async Task<PlacementDecision> EvaluateAsync(
            IReadOnlyDictionary<int, byte> pointsOfAskedQuestion,
            IReadOnlySet<int> wrongQuestionIds,
            decimal passPercentage,
            CancellationToken cancellationToken = default)
        {
            var levels = await _levels.GetLevelsInOrderAsync(cancellationToken);

            if (levels.Count == 0)
                throw new BusinessRuleException("لا توجد مستويات متاحة لتحديد مستوى الطالب");

            var levelOfQuestion = await LoadLevelOfQuestionAsync(pointsOfAskedQuestion.Keys.ToList(), cancellationToken);

            var decision = Decide(levels, levelOfQuestion, pointsOfAskedQuestion, wrongQuestionIds, passPercentage);

            if (decision.Levels.First(o => o.Level.Id == decision.PlacedLevel.Id).QuestionsAsked == 0)
                _logger.LogWarning(
                    "Placement stopped at level {LevelId} ({LevelTitle}) because it has no placement questions, "
                  + "so every learner who masters the levels before it is capped there. Give that level an active "
                  + "LevelAssessment quiz with auto-graded questions.",
                    decision.PlacedLevel.Id, decision.PlacedLevel.Title);

            return decision;
        }

        /// <summary>
        /// Re-describes a stored placement. The placed level is the stored one; the
        /// per-level breakdown is recomputed from the attempt's saved answers with
        /// the threshold that was stored with it and the Points frozen in the
        /// attempt's snapshot.
        /// </summary>
        public async Task<PlacementResultDto> DescribeAsync(
            long attemptId,
            int placedLevelId,
            decimal scorePercentage,
            decimal passPercentage,
            DateTime placedAt,
            CancellationToken cancellationToken = default)
        {
            var pointsOfAskedQuestion = await _db.QuizAttemptQuestions
                .AsNoTracking()
                .Where(aq => aq.QuizAttemptId == attemptId && aq.QuestionType != QuestionTypes.Essay)
                .Select(aq => new { aq.QuestionId, aq.Points })
                .ToDictionaryAsync(aq => aq.QuestionId, aq => aq.Points, cancellationToken);

            var wrongQuestionIds = (await _db.QuizAttemptMistakes
                    .AsNoTracking()
                    .Where(m => m.QuizAttemptId == attemptId)
                    .Select(m => m.QuestionId)
                    .ToListAsync(cancellationToken))
                .ToHashSet();

            var levels = await _levels.GetLevelsInOrderAsync(cancellationToken);

            if (levels.Count == 0)
                return new PlacementResultDto
                {
                    LevelId = placedLevelId,
                    ScorePercentage = scorePercentage,
                    PassPercentage = passPercentage,
                    PlacedAt = placedAt
                };

            var levelOfQuestion = await LoadLevelOfQuestionAsync(pointsOfAskedQuestion.Keys.ToList(), cancellationToken);

            return ToResultDto(
                Decide(levels, levelOfQuestion, pointsOfAskedQuestion, wrongQuestionIds, passPercentage),
                placedLevelId,
                scorePercentage,
                placedAt);
        }

        /// <summary>
        /// The placement rule. A level is mastered when at least one of its
        /// questions was asked and the child's score on them — the Points of the
        /// ones answered correctly ÷ the Points of all of them — reached
        /// <paramref name="passPercentage"/>. With every question worth 1 that is
        /// simply correct ÷ asked. The child is placed at the first level, in
        /// learning order, that is not mastered — a level with no placement
        /// questions cannot be shown mastered, so the test never skips a child past
        /// a level it could not check. Mastering every level places them at the last.
        /// </summary>
        public static PlacementDecision Decide(
            IReadOnlyList<LevelSummary> levelsInOrder,
            IReadOnlyDictionary<int, int> levelOfQuestion,
            IReadOnlyDictionary<int, byte> pointsOfQuestion,
            IReadOnlySet<int> wrongQuestionIds,
            decimal passPercentage)
        {
            if (levelsInOrder.Count == 0)
                throw new ArgumentException("At least one level is required.", nameof(levelsInOrder));

            var outcomes = new List<PlacementLevelOutcome>(levelsInOrder.Count);

            foreach (var level in levelsInOrder)
            {
                var asked = levelOfQuestion
                    .Where(q => q.Value == level.Id)
                    .Select(q => q.Key)
                    .ToList();

                var correct = 0;
                var totalPoints = 0;
                var earnedPoints = 0;

                foreach (var questionId in asked)
                {
                    // Both maps are built from the same attempt snapshot, so a
                    // question without Points is a caller bug, not a zero-point question.
                    if (!pointsOfQuestion.TryGetValue(questionId, out var points))
                        throw new ArgumentException(
                            $"No Points were supplied for question {questionId}.", nameof(pointsOfQuestion));

                    totalPoints += points;

                    if (wrongQuestionIds.Contains(questionId))
                        continue;

                    correct++;
                    earnedPoints += points;
                }

                var score = totalPoints == 0
                    ? 0m
                    : Math.Round(earnedPoints * 100m / totalPoints, 2, MidpointRounding.AwayFromZero);

                outcomes.Add(new PlacementLevelOutcome(
                    level, asked.Count, correct, totalPoints, earnedPoints, score,
                    asked.Count > 0 && score >= passPercentage));
            }

            var placed = outcomes.FirstOrDefault(o => !o.Mastered)?.Level
                         ?? levelsInOrder[levelsInOrder.Count - 1];

            return new PlacementDecision(placed, outcomes, passPercentage);
        }

        public static PlacementResultDto ToResultDto(
            PlacementDecision decision,
            int placedLevelId,
            decimal scorePercentage,
            DateTime placedAt)
        {
            var placedLevel = decision.Levels
                .Select(o => o.Level)
                .FirstOrDefault(l => l.Id == placedLevelId);

            return new PlacementResultDto
            {
                LevelId = placedLevelId,
                LevelTitle = placedLevel?.Title,
                LevelOrder = placedLevel?.Order,
                ScorePercentage = scorePercentage,
                PassPercentage = decision.PassPercentage,
                PlacedAt = placedAt,
                Levels = decision.Levels
                    .Where(o => o.QuestionsAsked > 0)
                    .Select(o => new PlacementLevelResultDto
                    {
                        LevelId = o.Level.Id,
                        LevelTitle = o.Level.Title,
                        QuestionsAsked = o.QuestionsAsked,
                        CorrectAnswers = o.CorrectAnswers,
                        TotalPoints = o.TotalPoints,
                        EarnedPoints = o.EarnedPoints,
                        ScorePercentage = o.ScorePercentage,
                        Mastered = o.Mastered
                    })
                    .ToList()
            };
        }

        /// <summary>
        /// Question → level, through the question's LevelAssessment quiz. Stable
        /// over time: a question cannot move to another quiz, and a quiz cannot be
        /// re-pointed at another level (neither is in the update DTOs).
        /// </summary>
        private async Task<Dictionary<int, int>> LoadLevelOfQuestionAsync(
            IReadOnlyCollection<int> questionIds,
            CancellationToken cancellationToken)
        {
            var ids = questionIds.ToList();

            var rows = await _db.Questions
                .AsNoTracking()
                .Where(q => ids.Contains(q.Id) && q.Quiz.LevelId != null)
                .Select(q => new { q.Id, q.Quiz.LevelId })
                .ToListAsync(cancellationToken);

            return rows.ToDictionary(r => r.Id, r => r.LevelId!.Value);
        }
    }

    public sealed record PlacementLevelOutcome(
        LevelSummary Level,
        int QuestionsAsked,
        int CorrectAnswers,
        int TotalPoints,
        int EarnedPoints,
        decimal ScorePercentage,
        bool Mastered);

    public sealed record PlacementDecision(
        LevelSummary PlacedLevel,
        IReadOnlyList<PlacementLevelOutcome> Levels,
        decimal PassPercentage);
}
