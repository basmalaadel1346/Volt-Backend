using AssessmentBL.Services.Constants;
using AssessmentDA.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shared.Assessment;

namespace AssessmentBL.Services
{
    /// <summary>
    /// Answers the Content module's unlocking question: which of these lessons
    /// has the child already passed the quiz for?
    ///
    /// "Passed" is a completed attempt of the lesson's active quiz that scored at
    /// least Assessment:LessonQuizPassPercentage. Any attempt counts, retries
    /// included — a child who got it right on the second try has learned it.
    ///
    /// A lesson whose quiz does not exist, is a draft, or has no active questions
    /// is reported as passed: gating a child behind a quiz nobody wrote would
    /// lock the course on an admin's omission.
    /// </summary>
    public sealed class LessonQuizGateService : ILessonQuizGate
    {
        private readonly AssessmentDbContext _db;
        private readonly AssessmentSettings _settings;

        public LessonQuizGateService(AssessmentDbContext db, IOptions<AssessmentSettings> settings)
        {
            _db = db;
            _settings = settings.Value;
        }

        public async Task<IReadOnlySet<int>> GetPassedLessonIdsAsync(
            Guid userId,
            IReadOnlyCollection<int> lessonIds,
            CancellationToken cancellationToken = default)
        {
            if (lessonIds.Count == 0)
                return new HashSet<int>();

            var ids = lessonIds.Distinct().ToList();

            // The quiz that actually gates each lesson: active, with active
            // questions, newest wins — the same one GET /for-lesson serves.
            var gates = await _db.Quizzes
                .AsNoTracking()
                .Where(q => q.QuizType == QuizTypes.LessonQuiz
                         && q.IsActive
                         && q.LessonId != null
                         && ids.Contains(q.LessonId.Value)
                         && q.Questions.Any(question => question.IsActive))
                .Select(q => new { QuizId = q.Id, LessonId = q.LessonId!.Value })
                .ToListAsync(cancellationToken);

            var quizIdByLesson = gates
                .GroupBy(g => g.LessonId)
                .ToDictionary(g => g.Key, g => g.Max(x => x.QuizId));

            // A lesson with no gating quiz is open by default.
            var passed = ids.Where(id => !quizIdByLesson.ContainsKey(id)).ToHashSet();

            if (quizIdByLesson.Count == 0)
                return passed;

            var gateQuizIds = quizIdByLesson.Values.ToList();
            var passMark = _settings.EffectiveLessonQuizPassPercentage;

            var clearedQuizIds = await _db.QuizAttempts
                .AsNoTracking()
                .Where(a => a.UserId == userId
                         && a.Status == QuizAttemptStatuses.Completed
                         && a.ScorePercentage >= passMark
                         && gateQuizIds.Contains(a.QuizId))
                .Select(a => a.QuizId)
                .Distinct()
                .ToListAsync(cancellationToken);

            var cleared = clearedQuizIds.ToHashSet();

            foreach (var (lessonId, quizId) in quizIdByLesson)
            {
                if (cleared.Contains(quizId))
                    passed.Add(lessonId);
            }

            return passed;
        }
    }
}
