using Shared.Common.Text;

namespace AssessmentBL.Services.Constants
{
    /// <summary>Mirrors CK_Topics_LearningLevel, in the order a child meets them.</summary>
    public static class TopicLearningLevels
    {
        public const string Beginner = "Beginner";
        public const string Intermediate = "Intermediate";
        public const string Advanced = "Advanced";

        public static readonly IReadOnlyList<string> All = [Beginner, Intermediate, Advanced];

        /// <summary>
        /// The canonical spelling of <paramref name="learningLevel"/> whatever its
        /// casing, or null when it is not a learning level.
        /// </summary>
        public static string? Normalize(string? learningLevel) => CanonicalValues.Match(All, learningLevel);
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

    /// <summary>
    /// The hidden catch-all every question with no topic is reported under, so
    /// the progress map always adds up to TotalXp and the app never has to show
    /// a total it cannot break down. It is not a row in Assessment.Categories:
    /// no admin creates, edits or deletes it, and no question can be assigned to
    /// it — it exists only in the progress response.
    /// </summary>
    public static class UncategorizedProgress
    {
        /// <summary>
        /// Zero, which Categories.Id (TINYINT IDENTITY-free, seeded from 1) never
        /// uses, so it can never collide with a real category.
        /// </summary>
        public const byte CategoryId = 0;

        /// <summary>Sorted after every real category, whose SortOrder is a SMALLINT.</summary>
        public const short SortOrder = short.MaxValue;

        public static string Name(string language) =>
            language == ContentLanguages.English ? "General" : "عام";
    }
}
