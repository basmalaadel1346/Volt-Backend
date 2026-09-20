using AssessmentBL.DTOs.Placement;
using AssessmentBL.DTOs.QuizAttempt;
using AssessmentBL.Interfaces;
using AssessmentBL.Services.Constants;
using AssessmentDA.Context;
using Microsoft.EntityFrameworkCore;

namespace AssessmentBL.Services
{
    public class PlacementService : IPlacementService
    {
        private readonly AssessmentDbContext _db;
        private readonly IQuizAttemptService _attempts;
        private readonly PlacementEngine _placement;

        public PlacementService(
            AssessmentDbContext db,
            IQuizAttemptService attempts,
            PlacementEngine placement)
        {
            _db = db;
            _attempts = attempts;
            _placement = placement;
        }

        public async Task<PlacementStatusDto> GetStatusAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            var stored = await _db.UserPlacements
                .AsNoTracking()
                .Where(p => p.UserId == userId)
                .Select(p => new { p.QuizAttemptId, p.PlacedLevelId, p.ScorePercentage, p.PassPercentage, p.PlacedAt })
                .FirstOrDefaultAsync(cancellationToken);

            if (stored is not null)
                return new PlacementStatusDto
                {
                    Status = PlacementStatuses.Completed,
                    AttemptId = stored.QuizAttemptId,
                    Result = await _placement.DescribeAsync(
                        stored.QuizAttemptId,
                        stored.PlacedLevelId,
                        stored.ScorePercentage,
                        stored.PassPercentage,
                        stored.PlacedAt,
                        cancellationToken)
                };

            var quizId = await _placement.FindActivePlacementQuizIdAsync(cancellationToken);

            if (quizId is null)
                return new PlacementStatusDto { Status = PlacementStatuses.Unavailable };

            // An open attempt is resumable whatever has happened to the question
            // pool since it started, so it is checked before the pool is.
            var openAttemptId = await _placement.FindResumableAttemptIdAsync(userId, quizId.Value, cancellationToken);

            if (openAttemptId is not null)
                return new PlacementStatusDto
                {
                    Status = PlacementStatuses.InProgress,
                    PlacementQuizId = quizId,
                    AttemptId = openAttemptId
                };

            // No level has assessment questions to sample: tell the app to skip
            // rather than show a test that cannot start.
            if ((await _placement.SelectQuestionIdsAsync(cancellationToken)).Count == 0)
                return new PlacementStatusDto { Status = PlacementStatuses.Unavailable };

            // A learner who already completed quizzes before placement existed is
            // not forced through it; a brand-new learner is.
            var hasHistory = await _db.QuizAttempts
                .AsNoTracking()
                .AnyAsync(a => a.UserId == userId && a.Status == QuizAttemptStatuses.Completed, cancellationToken);

            return new PlacementStatusDto
            {
                Status = hasHistory ? PlacementStatuses.Optional : PlacementStatuses.Required,
                PlacementQuizId = quizId
            };
        }

        public async Task<QuizAttemptResponseDto> StartAsync(
            Guid userId,
            string? language = null,
            CancellationToken cancellationToken = default)
        {
            var quizId = await _placement.FindActivePlacementQuizIdAsync(cancellationToken)
                ?? throw new KeyNotFoundException("لا يوجد اختبار تحديد مستوى متاح حاليًا");

            // The attempt engine owns the placement rules for starting: it refuses a
            // learner who is already placed and resumes an open placement attempt.
            return await _attempts.StartAsync(quizId, userId, previousAttemptId: null, language, cancellationToken);
        }
    }
}
