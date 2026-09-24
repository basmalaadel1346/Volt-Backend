using AssessmentBL.DTOs.LevelSkip;
using AssessmentBL.DTOs.QuizAttempt;

namespace AssessmentBL.Interfaces
{
    public interface ILevelSkipService
    {
        /// <summary>
        /// Whether the level-skip challenge is offered for a level, and on what
        /// terms (questions, clock, hearts, pass mark).
        /// </summary>
        Task<LevelSkipStatusDto> GetStatusAsync(
            int levelId,
            Guid userId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Starts the challenge, or resumes the learner's open one. Submitted like
        /// any attempt (POST /api/quiz-attempts/{id}/submit); the result carries
        /// the verdict in levelSkip.
        /// </summary>
        Task<QuizAttemptResponseDto> StartAsync(
            int levelId,
            Guid userId,
            string? language = null,
            CancellationToken cancellationToken = default);
    }
}
