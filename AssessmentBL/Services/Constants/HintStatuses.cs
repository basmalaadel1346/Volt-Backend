namespace AssessmentBL.Services.Constants
{
    /// <summary>
    /// Tells the client whether the retry questions of a result carry AI hints.
    /// Hints are optional: the score in the same response is final whatever
    /// this says.
    /// </summary>
    public static class HintStatuses
    {
        /// <summary>No wrong answers, so there is nothing to hint.</summary>
        public const string NotRequired = "NotRequired";

        /// <summary>
        /// There are wrong answers and the AI has not finished writing their hints
        /// yet. The only non-final status: the submission hands the work to the
        /// background and returns, so this is what a result says until the hints
        /// land. Ask GET /api/quiz-attempts/{id}/retry-questions again, or wait for
        /// the hints-ready push on the learner hub.
        /// </summary>
        public const string Pending = "Pending";

        /// <summary>Every retry question carries a hint.</summary>
        public const string Generated = "Generated";

        /// <summary>Some retry questions carry a hint; the rest have CurrentHint = null.</summary>
        public const string Partial = "Partial";

        /// <summary>
        /// No hint is available — the AI was unavailable, timed out, is not
        /// configured, or returned nothing usable.
        /// </summary>
        public const string Unavailable = "Unavailable";

        /// <param name="hintingSettled">
        /// False while the background hint job for this attempt has not run yet:
        /// "no hints saved" then means "not written yet" (Pending), not "the AI
        /// had nothing to give" (Unavailable).
        /// </param>
        public static string Resolve(int retryQuestions, int hintedQuestions, bool hintingSettled = true)
        {
            if (retryQuestions <= 0)
                return NotRequired;

            if (hintedQuestions >= retryQuestions)
                return Generated;

            if (!hintingSettled)
                return Pending;

            return hintedQuestions <= 0 ? Unavailable : Partial;
        }
    }
}
