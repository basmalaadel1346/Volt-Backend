namespace AssessmentBL.DTOs.QuizAttempt
{
    /// <summary>
    /// The questions a child got wrong in one attempt, with the hint that goes
    /// with each. Its own endpoint rather than part of the result, because the
    /// hints are written by the AI AFTER the result is committed: folding them
    /// into the submit response is what used to make that request wait on the AI.
    /// GET it when the child taps "try again", or when the hints-ready push
    /// arrives on the learner hub.
    /// </summary>
    public class RetryQuestionsDto
    {
        public long AttemptId { get; set; }

        /// <summary>Start the retry with POST /api/quiz-attempts?quizId=…&amp;previousAttemptId=…</summary>
        public int QuizId { get; set; }

        /// <summary>
        /// NotRequired — nothing was wrong, so there is nothing to retry.
        /// Pending — the hints are still being written; ask again, or wait for the
        /// push. Generated | Partial | Unavailable — final.
        /// </summary>
        public string HintsStatus { get; set; } = null!;

        public string Language { get; set; } = null!;

        public bool LanguageFallbackApplied { get; set; }

        public List<QuizQuestionForAttemptDto> Questions { get; set; } = new();
    }
}
