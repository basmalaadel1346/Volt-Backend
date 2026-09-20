namespace AssessmentBL.Services.Constants
{
    /// <summary>Mirrors CK_QuizAttemptEssayAnswers_Status.</summary>
    public static class EssayAnswerStatuses
    {
        /// <summary>Waiting for the AI. The only state that is not final.</summary>
        public const string Pending = "Pending";

        /// <summary>The AI graded it: AwardedPoints and Feedback are set. Final.</summary>
        public const string Graded = "Graded";

        /// <summary>
        /// The AI declined to grade it, or could not produce a usable grade within
        /// the allowed attempts. Final: no points and no feedback. Nobody else
        /// grades it — essays are graded by the AI only.
        /// </summary>
        public const string NotGraded = "NotGraded";
    }

    /// <summary>Mirrors CK_QuizAttemptEssayAnswers_GradedBy. The AI is the only grader.</summary>
    public static class EssayGraders
    {
        public const string Ai = "Ai";
    }

    /// <summary>
    /// How the AI evaluation of an essay ended. Mirrors
    /// CK_QuizAttemptEssayAnswers_AiOutcome; null while the essay is Pending.
    /// </summary>
    public static class EssayAiOutcomes
    {
        /// <summary>The AI returned a valid grade; the essay is Graded.</summary>
        public const string Accepted = "Accepted";

        /// <summary>The AI answered "Skipped" for it; the essay is NotGraded.</summary>
        public const string Declined = "Declined";

        /// <summary>Every allowed attempt failed to produce a usable grade; the essay is NotGraded.</summary>
        public const string Failed = "Failed";
    }
}
