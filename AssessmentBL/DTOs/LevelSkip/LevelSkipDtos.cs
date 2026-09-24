namespace AssessmentBL.DTOs.LevelSkip
{
    /// <summary>GET /api/level-skip/{levelId} — whether the child may try to skip a level.</summary>
    public class LevelSkipStatusDto
    {
        public int LevelId { get; set; }

        /// <summary>Available | InProgress | Passed | Unavailable.</summary>
        public string Status { get; set; } = null!;

        /// <summary>The quiz to start. Null when Unavailable.</summary>
        public int? QuizId { get; set; }

        /// <summary>The open attempt, when one is in progress.</summary>
        public long? AttemptId { get; set; }

        /// <summary>Questions the challenge will ask.</summary>
        public int QuestionCount { get; set; }

        public int TimeLimitSeconds { get; set; }

        public byte Hearts { get; set; }

        /// <summary>Score needed to skip the level, for a run that keeps a heart.</summary>
        public decimal PassPercentage { get; set; }

        /// <summary>How many times this child has already tried. 0 the first time.</summary>
        public int PreviousAttempts { get; set; }
    }

    /// <summary>What GET /api/level-skip/{levelId} tells the app to do.</summary>
    public static class LevelSkipStatuses
    {
        /// <summary>The challenge can be started now.</summary>
        public const string Available = "Available";

        /// <summary>An attempt is open: starting again resumes it.</summary>
        public const string InProgress = "InProgress";

        /// <summary>The level is already skipped (or finished normally). Nothing to do.</summary>
        public const string Passed = "Passed";

        /// <summary>No active level-skip quiz, or the level has nothing to sample.</summary>
        public const string Unavailable = "Unavailable";
    }
}
