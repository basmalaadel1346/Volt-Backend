namespace AssessmentBL.Services.Constants
{
    /// <summary>Mirrors CK_Topics_LearningLevel, in the order a child meets them.</summary>
    public static class TopicLearningLevels
    {
        public const string Beginner = "Beginner";
        public const string Intermediate = "Intermediate";
        public const string Advanced = "Advanced";

        public static readonly IReadOnlyList<string> All = [Beginner, Intermediate, Advanced];
    }

    /// <summary>
    /// How far a child is in a topic, as the progress screen shows it. Derived on
    /// every read from UserTopicStats, never stored. See <see cref="TopicProgress"/>.
    /// </summary>
    public static class TopicMasteryLevels
    {
        /// <summary>Nothing done in this topic yet.</summary>
        public const string NotStarted = "NotStarted";

        /// <summary>Started, and fewer than half of the answers are correct so far.</summary>
        public const string Learning = "Learning";

        /// <summary>At least half of the answers correct, not mastered yet.</summary>
        public const string Practicing = "Practicing";

        /// <summary>
        /// At least Assessment:TopicMasteryPercentage correct, over at least
        /// Assessment:TopicMasteryMinQuestions answers.
        /// </summary>
        public const string Mastered = "Mastered";
    }
}
