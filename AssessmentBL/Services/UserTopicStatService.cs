using AssessmentBL.DTOs.UserTopicStat;
using AssessmentBL.Interfaces;
using AssessmentDA.Context;
using AssessmentDA.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shared.Common.Abstractions;
using Shared.Common.Exceptions;
using System.Linq.Expressions;
using AssessmentBL.Services.Constants;

namespace AssessmentBL.Services
{
    public class UserTopicStatService : IUserTopicStatService
    {
        private readonly AssessmentDbContext _db;
        private readonly IDateTimeProvider _clock;
        private readonly AssessmentSettings _settings;

        public UserTopicStatService(
            AssessmentDbContext db,
            IDateTimeProvider clock,
            IOptions<AssessmentSettings> settings)
        {
            _db = db;
            _clock = clock;
            _settings = settings.Value;
        }

        /// <summary>
        /// The progress map. Every active topic of an active category is on it,
        /// practised or not, so the child sees what is still ahead; a topic the
        /// child practised stays on it even after an admin retires it, so earned
        /// progress never disappears.
        /// </summary>
        public async Task<MyProgressResponseDto> GetMyProgressAsync(
            Guid userId,
            string? language = null,
            CancellationToken cancellationToken = default)
        {
            var resolvedLanguage = ContentLanguages.Normalize(language);

            var stats = await LoadStatsAsync(userId, topicId: null, cancellationToken);
            var xp = await LoadXpAsync(userId, topicId: null, cancellationToken);

            var practisedTopicIds = stats
                .Select(s => s.TopicId)
                .Concat(xp.Where(x => x.TopicId != null).Select(x => x.TopicId!.Value))
                .Distinct()
                .ToList();

            var topics = await LoadTopicsAsync(
                t => (t.IsActive && t.Category.IsActive) || practisedTopicIds.Contains(t.Id),
                resolvedLanguage,
                cancellationToken);

            var topicProgress = topics
                .OrderBy(t => t.CategorySortOrder)
                .ThenBy(t => t.CategoryId)
                .ThenBy(t => LearningLevelRank(t.LearningLevel))
                .ThenBy(t => t.Id)
                .Select(t => BuildTopic(
                    t,
                    stats.Where(s => s.TopicId == t.Id).ToList(),
                    xp.Where(x => x.TopicId == t.Id).ToList(),
                    resolvedLanguage))
                .ToList();

            var answered = topicProgress.Sum(t => t.QuestionsAnswered);
            var correct = topicProgress.Sum(t => t.CorrectAnswers);
            var mastered = topicProgress.Count(t => t.Mastery == TopicMasteryLevels.Mastered);
            var started = topicProgress.Count(t => t.Mastery != TopicMasteryLevels.NotStarted);

            // Not the sum of the topics: a question with no topic earns XP too.
            var totalXp = xp.Sum(x => x.Xp);

            return new MyProgressResponseDto
            {
                Language = resolvedLanguage,
                TotalXp = totalXp,
                TotalTopics = topicProgress.Count,
                TopicsStarted = started,
                TopicsMastered = mastered,
                QuestionsAnswered = answered,
                CorrectAnswers = correct,
                AccuracyPercentage = TopicProgress.AccuracyPercentage(answered, correct),
                HintsUsed = topicProgress.Sum(t => t.HintsUsed),
                Message = TopicProgress.Headline(
                    totalXp, mastered, topicProgress.Count, started > 0 || totalXp > 0, resolvedLanguage),
                Categories = topicProgress
                    .GroupBy(t => t.CategoryId)
                    .Select(g => new CategoryProgressDto
                    {
                        CategoryId = g.Key,
                        Name = g.First().CategoryName,
                        Xp = g.Sum(t => t.Xp),
                        TotalTopics = g.Count(),
                        TopicsStarted = g.Count(t => t.Mastery != TopicMasteryLevels.NotStarted),
                        TopicsMastered = g.Count(t => t.Mastery == TopicMasteryLevels.Mastered),
                        Topics = g.ToList()
                    })
                    .ToList()
            };
        }

        public async Task<TopicProgressDto> GetTopicProgressAsync(
            Guid userId,
            int topicId,
            string? language = null,
            CancellationToken cancellationToken = default)
        {
            var resolvedLanguage = ContentLanguages.Normalize(language);

            var topic = (await LoadTopicsAsync(t => t.Id == topicId, resolvedLanguage, cancellationToken))
                .FirstOrDefault()
                ?? throw new KeyNotFoundException($"الموضوع رقم {topicId} غير موجود");

            var stats = await LoadStatsAsync(userId, topicId, cancellationToken);
            var xp = await LoadXpAsync(userId, topicId, cancellationToken);

            return BuildTopic(topic, stats, xp, resolvedLanguage);
        }

        private TopicProgressDto BuildTopic(
            TopicRow topic,
            IReadOnlyList<StatRow> stats,
            IReadOnlyList<XpRow> xp,
            string language)
        {
            // Difficulty buckets the child has touched: counted answers, XP, or both
            // (a topic practised only through essays has XP and no counts).
            var difficulties = QuestionDifficulties.All
                .Select(difficulty => new
                {
                    Difficulty = difficulty,
                    Stat = stats.FirstOrDefault(s => s.Difficulty == difficulty),
                    Xp = xp.Where(x => x.Difficulty == difficulty).Sum(x => x.Xp)
                })
                .Where(d => d.Stat is not null || d.Xp > 0)
                .Select(d => new DifficultyProgressDto
                {
                    Difficulty = d.Difficulty,
                    Xp = d.Xp,
                    QuestionsAnswered = d.Stat?.Answered ?? 0,
                    CorrectAnswers = d.Stat?.Correct ?? 0,
                    WrongAnswers = (d.Stat?.Answered ?? 0) - (d.Stat?.Correct ?? 0),
                    AccuracyPercentage = TopicProgress.AccuracyPercentage(d.Stat?.Answered ?? 0, d.Stat?.Correct ?? 0),
                    HintsUsed = d.Stat?.HintsUsed ?? 0
                })
                .ToList();

            var answered = difficulties.Sum(d => d.QuestionsAnswered);
            var correct = difficulties.Sum(d => d.CorrectAnswers);
            var hints = difficulties.Sum(d => d.HintsUsed);
            var topicXp = difficulties.Sum(d => d.Xp);

            var mastery = TopicProgress.Mastery(
                answered,
                correct,
                practised: topicXp > 0 || hints > 0,
                _settings.EffectiveTopicMasteryPercentage,
                _settings.EffectiveTopicMasteryMinQuestions);

            return new TopicProgressDto
            {
                TopicId = topic.Id,
                Name = topic.Name,
                Description = topic.Description,
                CategoryId = topic.CategoryId,
                CategoryName = topic.CategoryName,
                LearningLevel = topic.LearningLevel,
                Mastery = mastery,
                Stars = TopicProgress.Stars(mastery),
                Xp = topicXp,
                QuestionsAnswered = answered,
                CorrectAnswers = correct,
                WrongAnswers = answered - correct,
                AccuracyPercentage = TopicProgress.AccuracyPercentage(answered, correct),
                HintsUsed = hints,
                LastPracticedAt = stats.Max(s => s.LastPracticedAt),
                Message = TopicProgress.TopicMessage(mastery, answered, correct, language),
                Difficulties = difficulties
            };
        }

        /// <summary>Topics with their names in the requested language (requested → fallback → base).</summary>
        private Task<List<TopicRow>> LoadTopicsAsync(
            Expression<Func<Topic, bool>> filter,
            string language,
            CancellationToken cancellationToken) =>
            _db.Topics
                .AsNoTracking()
                .Where(filter)
                .Select(t => new TopicRow
                {
                    Id = t.Id,
                    Name =
                        t.TopicTranslations
                            .Where(tr => tr.LanguageCode == language)
                            .Select(tr => tr.Name)
                            .FirstOrDefault()
                        ?? t.TopicTranslations
                            .Where(tr => tr.LanguageCode == ContentLanguages.Fallback)
                            .Select(tr => tr.Name)
                            .FirstOrDefault()
                        ?? t.Name,
                    Description =
                        t.TopicTranslations
                            .Where(tr => tr.LanguageCode == language)
                            .Select(tr => tr.Description)
                            .FirstOrDefault()
                        ?? t.TopicTranslations
                            .Where(tr => tr.LanguageCode == ContentLanguages.Fallback)
                            .Select(tr => tr.Description)
                            .FirstOrDefault()
                        ?? t.Description,
                    LearningLevel = t.LearningLevel,
                    CategoryId = t.CategoryId,
                    CategorySortOrder = t.Category.SortOrder,
                    CategoryName =
                        t.Category.CategoryTranslations
                            .Where(tr => tr.LanguageCode == language)
                            .Select(tr => tr.Name)
                            .FirstOrDefault()
                        ?? t.Category.CategoryTranslations
                            .Where(tr => tr.LanguageCode == ContentLanguages.Fallback)
                            .Select(tr => tr.Name)
                            .FirstOrDefault()
                        ?? t.Category.Name
                })
                .ToListAsync(cancellationToken);

        private Task<List<StatRow>> LoadStatsAsync(Guid userId, int? topicId, CancellationToken cancellationToken) =>
            _db.UserTopicStats
                .AsNoTracking()
                .Where(s => s.UserId == userId && (topicId == null || s.TopicId == topicId))
                .Select(s => new StatRow
                {
                    TopicId = s.TopicId,
                    Difficulty = s.Difficulty,
                    Answered = s.QuestionsAnsweredCount,
                    Correct = s.CorrectCount,
                    HintsUsed = s.HintsUsedCount,
                    LastPracticedAt = s.LastPracticedAt
                })
                .ToListAsync(cancellationToken);

        /// <summary>
        /// XP per (topic, difficulty) — topic null for questions that have none —
        /// read from the attempt history, so it is always exactly what the results
        /// said: a correct MultipleChoice/TrueFalse answer earns its snapshot Points,
        /// a graded essay its AwardedPoints. Pending and NotGraded essays earn nothing
        /// (yet). Every submitted attempt counts, retries included: each correct
        /// answer is XP earned, like each finished lesson in any XP game.
        /// </summary>
        private async Task<List<XpRow>> LoadXpAsync(Guid userId, int? topicId, CancellationToken cancellationToken)
        {
            // Only wrong answers are stored, so "correct" is "no mistake row".
            var autoGraded = await _db.QuizAttemptQuestions
                .AsNoTracking()
                .Where(aq => aq.QuizAttempt.UserId == userId
                          && aq.QuizAttempt.Status == QuizAttemptStatuses.Completed
                          && aq.QuestionType != QuestionTypes.Essay
                          && (topicId == null || aq.TopicId == topicId)
                          && !_db.QuizAttemptMistakes.Any(
                              m => m.QuizAttemptId == aq.QuizAttemptId && m.QuestionId == aq.QuestionId))
                .GroupBy(aq => new { aq.TopicId, aq.Difficulty })
                .Select(g => new XpRow
                {
                    TopicId = g.Key.TopicId,
                    Difficulty = g.Key.Difficulty,
                    Xp = g.Sum(aq => (int)aq.Points)
                })
                .ToListAsync(cancellationToken);

            var essays = await _db.QuizAttemptEssayAnswers
                .AsNoTracking()
                .Where(e => e.QuizAttempt.UserId == userId && e.Status == EssayAnswerStatuses.Graded)
                .Join(
                    _db.QuizAttemptQuestions,
                    e => new { e.QuizAttemptId, e.QuestionId },
                    aq => new { aq.QuizAttemptId, aq.QuestionId },
                    (e, aq) => new { aq.TopicId, aq.Difficulty, Points = e.AwardedPoints ?? 0 })
                .Where(x => topicId == null || x.TopicId == topicId)
                .GroupBy(x => new { x.TopicId, x.Difficulty })
                .Select(g => new XpRow
                {
                    TopicId = g.Key.TopicId,
                    Difficulty = g.Key.Difficulty,
                    Xp = g.Sum(x => x.Points)
                })
                .ToListAsync(cancellationToken);

            return autoGraded.Concat(essays).ToList();
        }

        private static int LearningLevelRank(string learningLevel)
        {
            var rank = TopicLearningLevels.All.ToList().IndexOf(learningLevel);
            return rank < 0 ? int.MaxValue : rank;
        }

        /// <summary>
        /// Folds one completed attempt into the (UserId, TopicId, Difficulty)
        /// aggregates. Counts come from that attempt's own QuizAttemptQuestion
        /// rows, so a retry contributes only the questions it actually
        /// contained. WrongCount is never assigned — SQL Server computes it.
        ///
        /// A fixed number of round trips whatever the attempt size: every read
        /// happens before the bucket loop, and the loop itself is in-memory only.
        /// </summary>
        public async Task UpdateAfterQuizAttemptAsync(
            long quizAttemptId,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            // Filtered on the owner: another user's attempt is reported exactly like
            // one that does not exist, so a 404 never confirms that an id is real.
            var attempt = await _db.QuizAttempts
                .AsNoTracking()
                .Where(a => a.Id == quizAttemptId && a.UserId == userId)
                .Select(a => new
                {
                    a.Id,
                    a.Status,
                    a.CompletedAt
                })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException($"المحاولة رقم {quizAttemptId} غير موجودة");

            if (attempt.Status != QuizAttemptStatuses.Completed)
                throw new BusinessRuleException(
                    $"المحاولة رقم {quizAttemptId} لم تكتمل بعد");

            // The authoritative set of questions this attempt contained, with
            // the classification frozen at attempt start. Reading TopicId /
            // Difficulty from the live Question row here would re-attribute
            // this attempt's counters if an admin later re-classified it.
            //
            // These are COUNTS of answers per topic, not points: a question's
            // Points weigh the score, not the child's mastery of a topic.
            //
            // Essay answers are not auto-graded, so counting them as "answered"
            // with no possible "correct" would permanently depress the child's
            // mastery for that topic. The AI grades them later, after these
            // counters are committed, so they are never counted here.
            //
            // A question with no topic has no bucket to count in. It still earns
            // XP, which is read from the attempt history, not from these rows.
            var attemptQuestions = await _db.QuizAttemptQuestions
                .AsNoTracking()
                .Where(aq => aq.QuizAttemptId == quizAttemptId
                          && aq.QuestionType != QuestionTypes.Essay
                          && aq.TopicId != null)
                .Select(aq => new
                {
                    aq.QuestionId,
                    TopicId = aq.TopicId!.Value,
                    aq.Difficulty
                })
                .ToListAsync(cancellationToken);

            if (attemptQuestions.Count == 0)
                return;

            // Every question this attempt got wrong, fetched ONCE. This replaces a
            // per-question _db.QuizAttemptMistakes.Any(...) that ran synchronously
            // inside the grouping below — one blocking query per question.
            var wrongQuestionIds = (await _db.QuizAttemptMistakes
                    .AsNoTracking()
                    .Where(m => m.QuizAttemptId == quizAttemptId)
                    .Select(m => m.QuestionId)
                    .ToListAsync(cancellationToken))
                .ToHashSet();

            var topicIds = attemptQuestions.Select(q => q.TopicId).Distinct().ToList();

            var hintCounts = await CountHintsByBucketAsync(userId, topicIds, cancellationToken);

            var buckets = attemptQuestions
                .GroupBy(q => (q.TopicId, q.Difficulty))
                .Select(g => new
                {
                    g.Key.TopicId,
                    g.Key.Difficulty,
                    Answered = g.Count(),
                    Correct = g.Count(q => !wrongQuestionIds.Contains(q.QuestionId)),
                    Hints = hintCounts.GetValueOrDefault(g.Key)
                })
                .ToList();

            // One tracked read for every row we might touch — no per-bucket query.
            var existingStats = await _db.UserTopicStats
                .Where(s => s.UserId == userId && topicIds.Contains(s.TopicId))
                .ToDictionaryAsync(s => (s.TopicId, s.Difficulty), cancellationToken);

            // CK_QuizAttempts_CompletedRequiresAllAnswered guarantees a
            // completed attempt has CompletedAt, so the fallback is belt-and-braces.
            var practisedAt = attempt.CompletedAt ?? _clock.UtcNow;

            foreach (var bucket in buckets)
            {
                if (!existingStats.TryGetValue((bucket.TopicId, bucket.Difficulty), out var stat))
                {
                    stat = new UserTopicStat
                    {
                        UserId = userId,
                        TopicId = bucket.TopicId,
                        Difficulty = bucket.Difficulty,
                        QuestionsAnsweredCount = 0,
                        CorrectCount = 0,
                        HintsUsedCount = 0
                    };

                    _db.UserTopicStats.Add(stat);
                }

                stat.QuestionsAnsweredCount += bucket.Answered;
                stat.CorrectCount += bucket.Correct;
                stat.HintsUsedCount = bucket.Hints;
                stat.LastQuizAttemptId = quizAttemptId;
                stat.LastPracticedAt = practisedAt;
                stat.UpdatedAt = practisedAt;
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        /// <summary>
        /// Re-derives HintsUsedCount for the buckets an attempt touched. Needed
        /// because hints are now saved AFTER the submission commits. Idempotent:
        /// the value is recomputed from saved hints, never accumulated.
        /// </summary>
        public async Task RefreshHintsUsedCountAsync(
            long quizAttemptId,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            var buckets = await _db.QuizAttemptQuestions
                .AsNoTracking()
                .Where(aq => aq.QuizAttemptId == quizAttemptId
                          && aq.QuizAttempt.UserId == userId
                          && aq.QuestionType != QuestionTypes.Essay
                          && aq.TopicId != null)
                .Select(aq => new { TopicId = aq.TopicId!.Value, aq.Difficulty })
                .Distinct()
                .ToListAsync(cancellationToken);

            if (buckets.Count == 0)
                return;

            var topicIds = buckets.Select(b => b.TopicId).Distinct().ToList();
            var touched = buckets.Select(b => (b.TopicId, b.Difficulty)).ToHashSet();

            var hintCounts = await CountHintsByBucketAsync(userId, topicIds, cancellationToken);

            var stats = await _db.UserTopicStats
                .Where(s => s.UserId == userId && topicIds.Contains(s.TopicId))
                .ToListAsync(cancellationToken);

            foreach (var stat in stats)
            {
                if (touched.Contains((stat.TopicId, stat.Difficulty)))
                    stat.HintsUsedCount = hintCounts.GetValueOrDefault((stat.TopicId, stat.Difficulty));
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        /// <summary>
        /// Hints the user has received, per (topic, difficulty) of the question
        /// they were for — limited to the topics being written. Only SAVED hints
        /// exist to count, and a hint is saved only when the child is shown it: a
        /// Hint-button press that came back Partial or Unavailable is never counted.
        /// </summary>
        private async Task<Dictionary<(int TopicId, string Difficulty), int>> CountHintsByBucketAsync(
            Guid userId,
            List<int> topicIds,
            CancellationToken cancellationToken)
        {
            // Through the hint's own (attempt, question) link, so Hint-button hints
            // — which have no mistake row — are counted too.
            var rows = await _db.QuestionHints
                .AsNoTracking()
                .Where(h => h.QuizAttemptQuestion.QuizAttempt.UserId == userId
                         && h.QuizAttemptQuestion.TopicId != null
                         && topicIds.Contains(h.QuizAttemptQuestion.TopicId!.Value))
                .GroupBy(h => new
                {
                    TopicId = h.QuizAttemptQuestion.TopicId!.Value,
                    h.QuizAttemptQuestion.Difficulty
                })
                .Select(g => new { g.Key.TopicId, g.Key.Difficulty, Count = g.Count() })
                .ToListAsync(cancellationToken);

            return rows.ToDictionary(x => (x.TopicId, x.Difficulty), x => x.Count);
        }

        private sealed class TopicRow
        {
            public int Id { get; init; }
            public string Name { get; init; } = null!;
            public string? Description { get; init; }
            public string LearningLevel { get; init; } = null!;
            public byte CategoryId { get; init; }
            public short CategorySortOrder { get; init; }
            public string CategoryName { get; init; } = null!;
        }

        private sealed class StatRow
        {
            public int TopicId { get; init; }
            public string Difficulty { get; init; } = null!;
            public int Answered { get; init; }
            public int Correct { get; init; }
            public int HintsUsed { get; init; }
            public DateTime? LastPracticedAt { get; init; }
        }

        private sealed class XpRow
        {
            public int? TopicId { get; init; }
            public string Difficulty { get; init; } = null!;
            public int Xp { get; init; }
        }
    }
}
