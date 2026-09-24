using Shared.Common.Text;

namespace AssessmentBL.Services.Constants
{
    /// <summary>Mirrors CK_Questions_Difficulty and CK_UserTopicStats_Difficulty.</summary>
    public static class QuestionDifficulties
    {
        public const string Easy = "Easy";
        public const string Medium = "Medium";
        public const string Hard = "Hard";
        public const string Advanced = "Advanced";

        // مصفوفة بتحتوي على كل المستويات عشان نستخدمها في الـ Validation
        public static readonly string[] All =
        {
            Easy,
            Medium,
            Hard,
            Advanced
        };

        /// <summary>
        /// The canonical spelling of <paramref name="difficulty"/> whatever its
        /// casing ("medium" → "Medium"), or null when it is not a difficulty.
        /// See <see cref="CanonicalValues"/>.
        /// </summary>
        public static string? Normalize(string? difficulty) => CanonicalValues.Match(All, difficulty);
    }
}
