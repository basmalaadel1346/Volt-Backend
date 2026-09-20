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
    }
}
