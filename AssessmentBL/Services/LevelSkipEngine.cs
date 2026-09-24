using AssessmentBL.Services.Constants;
using AssessmentDA.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shared.Content;

namespace AssessmentBL.Services
{
    /// <summary>
    /// The rules of the level-skip challenge: a child who already knows a level
    /// proves it once instead of sitting through every lesson.
    ///
    /// Like the placement test, the level-skip quiz owns no questions — it samples
    /// the level's own LessonQuiz quizzes, which are by definition what that level
    /// teaches. Short (about ten questions), against a three-minute clock and a
    /// few hearts, so it rewards knowing the material rather than working it out.
    ///
    /// Questions are spread ROUND-ROBIN across the level's lessons rather than
    /// taken lesson by lesson, so a child cannot pass by knowing only lesson one;
    /// and the sample is rotated by how many times this child has taken the
    /// challenge, so a second attempt is a different paper.
    /// </summary>
    public sealed class LevelSkipEngine
    {
        private readonly AssessmentDbContext _db;
        private readonly ILessonAvailability _lessons;
        private readonly AssessmentSettings _settings;

        public LevelSkipEngine(
            AssessmentDbContext db,
            ILessonAvailability lessons,
            IOptions<AssessmentSettings> settings)
        {
            _db = db;
            _lessons = lessons;
            _settings = settings.Value;
        }

        /// <summary>The active level-skip quiz of a level, if one is published.</summary>
        public Task<int?> FindActiveQuizIdAsync(int levelId, CancellationToken cancellationToken = default) =>
            _db.Quizzes
                .AsNoTracking()
                .Where(q => q.QuizType == QuizTypes.LevelSkip && q.LevelId == levelId && q.IsActive)
                .OrderByDescending(q => q.Id)
                .Select(q => (int?)q.Id)
                .FirstOrDefaultAsync(cancellationToken);

        /// <summary>
        /// The questions of one run of the challenge, in the order they are served.
        /// Empty when the level has nothing to sample, which the caller reports as
        /// "not available" rather than starting an empty attempt.
        /// </summary>
        public async Task<List<int>> SelectQuestionIdsAsync(
            int levelId,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            var lessons = await _lessons.GetPublishedLessonsAsync(levelId, cancellationToken);

            if (lessons.Count == 0)
                return [];

            var lessonIds = lessons.Select(l => l.Id).ToList();

            var quizzes = await _db.Quizzes
                .AsNoTracking()
                .Where(q => q.QuizType == QuizTypes.LessonQuiz
                         && q.IsActive
                         && q.LessonId != null
                         && lessonIds.Contains(q.LessonId.Value))
                .Select(q => new { q.Id, q.LessonId })
                .ToListAsync(cancellationToken);

            // Newest active quiz per lesson — the same tie-break GET /for-lesson uses.
            var quizIdByLesson = quizzes
                .GroupBy(q => q.LessonId!.Value)
                .ToDictionary(g => g.Key, g => g.Max(q => q.Id));

            if (quizIdByLesson.Count == 0)
                return [];

            var quizIds = quizIdByLesson.Values.ToList();

            // Auto-graded only (an Essay cannot be scored against the clock) and
            // answerable only (a question with no correct option would fail the
            // attempt snapshot).
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

            if (candidates.Count == 0)
                return [];

            // How many times this child has already taken the challenge for this
            // level. Rotating by it means a retake starts one question further into
            // each lesson, so it is a different paper without any randomness to
            // make a resumed attempt inconsistent.
            var previousRuns = await _db.QuizAttempts
                .AsNoTracking()
                .CountAsync(
                    a => a.UserId == userId
                      && a.Quiz.QuizType == QuizTypes.LevelSkip
                      && a.Quiz.LevelId == levelId
                      && a.Status != QuizAttemptStatuses.InProgress,
                    cancellationToken);

            var byQuiz = candidates
                .GroupBy(c => c.QuizId)
                .ToDictionary(g => g.Key, g => g.Select(c => c.Id).ToList());

            // Lesson order is the level's own teaching order.
            var pools = lessons
                .Where(l => quizIdByLesson.ContainsKey(l.Id))
                .Select(l => byQuiz.GetValueOrDefault(quizIdByLesson[l.Id]))
                .Where(pool => pool is { Count: > 0 })
                .Select(pool => pool!)
                .ToList();

            if (pools.Count == 0)
                return [];

            return TakeRoundRobin(pools, _settings.EffectiveLevelSkipQuestionCount, previousRuns);
        }

        /// <summary>
        /// One question from each pool in turn until <paramref name="count"/> are
        /// picked or every pool is exhausted, starting <paramref name="rotation"/>
        /// questions into each pool (wrapping). Pure, so the sampling rule is
        /// pinned by tests.
        /// </summary>
        public static List<int> TakeRoundRobin(
            IReadOnlyList<IReadOnlyList<int>> pools,
            int count,
            int rotation = 0)
        {
            var selected = new List<int>(count);
            var taken = new HashSet<int>();
            var deepest = pools.Count == 0 ? 0 : pools.Max(p => p.Count);

            for (var round = 0; round < deepest && selected.Count < count; round++)
            {
                foreach (var pool in pools)
                {
                    if (selected.Count >= count)
                        break;

                    if (pool.Count == 0)
                        continue;

                    // Modulo, so a rotation larger than the pool simply wraps.
                    var index = (round + rotation) % pool.Count;

                    // A question can sit in only one pool, so this only ever skips a
                    // slot a wrapped rotation has already used.
                    if (taken.Add(pool[index]))
                        selected.Add(pool[index]);
                }
            }

            return selected;
        }
    }
}
