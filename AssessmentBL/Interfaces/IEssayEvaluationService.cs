namespace AssessmentBL.Interfaces
{
    public interface IEssayEvaluationService
    {
        /// <summary>
        /// Evaluates this attempt's essay answers that are still waiting for the
        /// AI. Called right after a submission commits. AI problems never throw —
        /// an essay that could not be evaluated stays Pending for the background run.
        /// </summary>
        Task EvaluateAttemptAsync(long attemptId, CancellationToken cancellationToken);

        /// <summary>
        /// Background run: evaluates one batch of essays whose inline evaluation
        /// failed or never ran, respecting the retry delay and attempt limit.
        /// Returns how many reached a final state (Graded or NotGraded).
        /// </summary>
        Task<int> EvaluateDueAsync(CancellationToken cancellationToken);

        /// <summary>
        /// The grading deadline. Closes every essay answer still Pending past
        /// Assessment:EssayGradingDeadlineMinutes, whatever the AI is doing:
        /// graded from the question's keywords when it has them, NotGraded
        /// (TimedOut) when it does not.
        ///
        /// This is what guarantees an essay reaches a final state. Attempt limits
        /// alone did not: an AI that never answers, or that keeps failing in a way
        /// that does not consume an attempt, left the child looking at "Pending"
        /// with no end in sight. Returns how many answers were closed.
        /// </summary>
        Task<int> CloseOverdueAsync(CancellationToken cancellationToken);
    }
}
