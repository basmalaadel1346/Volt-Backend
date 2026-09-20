namespace AssessmentBL.Services.Constants
{
    /// <summary>What GET /api/placement tells the app to do.</summary>
    public static class PlacementStatuses
    {
        /// <summary>New learner with no placement: show the placement test now.</summary>
        public const string Required = "Required";

        /// <summary>No placement, but the learner already has completed quizzes
        /// (they joined before placement existed). The app may offer it.</summary>
        public const string Optional = "Optional";

        /// <summary>A placement attempt is open: POST /api/placement/start resumes it.</summary>
        public const string InProgress = "InProgress";

        /// <summary>Placed. The result is included.</summary>
        public const string Completed = "Completed";

        /// <summary>No active placement quiz, or no level has assessment questions
        /// to sample. The app skips placement.</summary>
        public const string Unavailable = "Unavailable";
    }
}
