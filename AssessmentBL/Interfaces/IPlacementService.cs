using AssessmentBL.DTOs.Placement;
using AssessmentBL.DTOs.QuizAttempt;

namespace AssessmentBL.Interfaces
{
    public interface IPlacementService
    {
        /// <summary>
        /// Whether the learner must, may, or already did take the placement test —
        /// what the app asks right after registration or login.
        /// </summary>
        Task<PlacementStatusDto> GetStatusAsync(
            Guid userId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Starts the placement test, or resumes the learner's open placement
        /// attempt. It is submitted like any attempt
        /// (POST /api/quiz-attempts/{id}/submit); the result carries the placement.
        /// </summary>
        Task<QuizAttemptResponseDto> StartAsync(
            Guid userId,
            string? language = null,
            CancellationToken cancellationToken = default);
    }
}
