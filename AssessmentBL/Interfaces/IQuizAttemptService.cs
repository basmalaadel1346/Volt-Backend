using AssessmentBL.DTOs.QuizAttempt;

namespace AssessmentBL.Interfaces
{
    public interface IQuizAttemptService
    {
        /// <summary>
        /// Starts a new quiz attempt.
        /// If previousAttemptId is null, this is the first attempt
        /// and all quiz questions are returned without hints.
        /// If previousAttemptId is provided, this is a retry attempt
        /// and only the previously incorrect questions are returned
        /// with their current AI-generated hints. A previousAttemptId that
        /// belongs to another user is reported as not found (404), like a
        /// missing one. Each question's Points are frozen in the attempt here.
        /// </summary>
        Task<QuizAttemptResponseDto> StartAsync(
            int quizId,
            Guid userId,
            long? previousAttemptId = null,
            string? language = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Submits the entire quiz attempt in bulk: grades it, completes it and
        /// updates the user's topic statistics in one committed transaction, and
        /// only then tries to attach AI hints. AI failure never fails the submit.
        /// Submitting an attempt that is already Completed returns the saved
        /// result unchanged; an expired or Abandoned attempt is rejected (410).
        /// Another user's attempt is reported as not found (404).
        /// </summary>
        Task<QuizAttemptResultDto> SubmitAsync(
            long attemptId,
            Guid userId,
            SubmitQuizAttemptDto dto,
            string? language = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves the details of a previous quiz attempt
        /// for the specified user. Another user's attempt is reported as not
        /// found (404), exactly like a missing one.
        /// </summary>
        Task<QuizAttemptResponseDto> GetByIdAsync(
            long attemptId,
            Guid userId,
            string? language = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the saved result of the user's own submitted attempt, so a
        /// client that never received the submit response can recover it.
        /// 409 while the attempt is still InProgress, 410 once it is Abandoned,
        /// 404 when it is missing or another user's.
        /// </summary>
        Task<QuizAttemptResultDto> GetResultAsync(
            long attemptId,
            Guid userId,
            string? language = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// The saved result of the user's most recently completed attempt, of any
        /// quiz (placement included) — the same result GetResultAsync returns for
        /// it. Attempts still in progress or abandoned have no result and are
        /// skipped. 404 when the user has never completed an attempt.
        /// </summary>
        Task<QuizAttemptResultDto> GetLatestResultAsync(
            Guid userId,
            string? language = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Marks every attempt that has stayed InProgress past the configured
        /// window as Abandoned. Idempotent. Returns how many attempts changed.
        /// </summary>
        Task<int> AbandonExpiredAttemptsAsync(CancellationToken cancellationToken = default);
    }
}
