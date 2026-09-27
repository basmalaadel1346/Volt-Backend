using AssessmentBL.DTOs.QuizAttempt;
using Shared.Common.BackgroundWork;

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
        /// Submits the entire quiz attempt in bulk: grades it, completes it,
        /// updates the user's topic statistics in one committed transaction and
        /// grants the gamification rewards — then RETURNS. The AI work (hints for
        /// the wrong answers, grading of the essays) is handed to
        /// IAttemptFollowUpQueue and runs on a background scope, so a child never
        /// waits on the AI for a score that is already final.
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
        /// The questions of a submitted attempt the child got wrong, each with its
        /// latest hint, and whether the hints are finished. Same ownership and
        /// status rules as GetResultAsync.
        /// </summary>
        Task<RetryQuestionsDto> GetRetryQuestionsAsync(
            long attemptId,
            Guid userId,
            string? language = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Runs one submitted attempt's optional AI work — hints, then essay
        /// grading — and pushes the results to the learner's app. Called by
        /// AttemptFollowUpWorker on a background scope, never from a request.
        /// Never throws: the result it belongs to is already committed.
        /// </summary>
        Task RunFollowUpAsync(AttemptFollowUp work, CancellationToken cancellationToken);

        /// <summary>
        /// Marks every attempt that has stayed InProgress past the configured
        /// window as Abandoned. Idempotent. Returns how many attempts changed.
        /// </summary>
        Task<int> AbandonExpiredAttemptsAsync(CancellationToken cancellationToken = default);
    }
}
