using AssessmentBL.DTOs.QuizAttempt;

namespace AssessmentBL.Interfaces
{
    public interface IHintService
    {
        /// <summary>
        /// The Hint button: one escalating hint for one question of an in-progress
        /// attempt the caller owns. The level comes from how many hints this
        /// attempt+question already has — the client cannot choose it.
        /// </summary>
        Task<HintResponseDto> RequestHintAsync(
            long attemptId,
            int questionId,
            Guid userId,
            string? language = null,
            CancellationToken cancellationToken = default);
    }
}
