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

        /// <summary>Every retry question carries a hint.</summary>
        public const string Generated = "Generated";

        /// <summary>Some retry questions carry a hint; the rest have CurrentHint = null.</summary>
        public const string Partial = "Partial";

        /// <summary>
        /// No hint is available — the AI was unavailable, timed out, is not
        /// configured, or returned nothing usable.
        /// </summary>
        public const string Unavailable = "Unavailable";

        public static string Resolve(int retryQuestions, int hintedQuestions)
        {
            if (retryQuestions <= 0)
                return NotRequired;

            if (hintedQuestions <= 0)
                return Unavailable;

            return hintedQuestions < retryQuestions ? Partial : Generated;
        }
    }
}
