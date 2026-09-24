using Shared.Common.Text;

namespace AssessmentBL.Services.Constants
{
    /// <summary>Mirrors CK_Questions_QuestionType.</summary>
    public static class QuestionTypes
    {
        public const string MultipleChoice = "MultipleChoice";
        public const string TrueFalse = "TrueFalse";
        public const string Essay = "Essay";

        public static readonly IReadOnlyList<string> All = [MultipleChoice, TrueFalse, Essay];

        /// <summary>
        /// True when the backend can score the answer itself. Essay cannot —
        /// the AI grades it after the submission, so it never enters ScorePercentage.
        /// </summary>
        public static bool IsAutoGraded(string questionType) => questionType != Essay;

        /// <summary>TrueFalse is a MultipleChoice with exactly two options.</summary>
        public static bool UsesOptions(string questionType) => questionType != Essay;

        /// <summary>
        /// The canonical spelling of <paramref name="questionType"/> whatever its
        /// casing ("multiplechoice" → "MultipleChoice"), or null when it is not a
        /// question type. See <see cref="CanonicalValues"/>.
        /// </summary>
        public static string? Normalize(string? questionType) => CanonicalValues.Match(All, questionType);
    }
}
