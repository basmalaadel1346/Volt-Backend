using Shared.Common.Text;

namespace AssessmentBL.Services.Constants
{
    public static class QuizTypes
    {
        public const string LevelAssessment = "LevelAssessment";
        public const string LessonQuiz = "LessonQuiz";
        public const string LessonReview = "LessonReview";
        public const string Standalone = "Standalone";

        /// <summary>
        /// The first-run placement test. Owns no questions: it samples each level's
        /// LevelAssessment quiz (see PlacementEngine). At most one is ACTIVE.
        /// </summary>
        public const string Placement = "Placement";

        /// <summary>
        /// The level-skip challenge. Owns no questions either: it samples the
        /// LessonQuiz quizzes of the level it skips (see LevelSkipEngine). Short,
        /// timed, and played on a fixed number of hearts. At most one is ACTIVE
        /// per level.
        /// </summary>
        public const string LevelSkip = "LevelSkip";

        // مصفوفة بتحتوي على كل الأنواع عشان نستخدمها في الـ Validation
        // في سطر: QuizTypes.Normalize(quizType)
        public static readonly string[] All =
        {
            LevelAssessment,
            LessonQuiz,
            LessonReview,
            Standalone,
            Placement,
            LevelSkip
        };

        /// <summary>
        /// A quiz that is generated from other quizzes' questions rather than
        /// owning its own. An admin may not add a question to one directly.
        /// </summary>
        public static bool IsSampled(string quizType) =>
            quizType is Placement or LevelSkip;

        /// <summary>
        /// The canonical spelling of <paramref name="quizType"/> whatever its
        /// casing, or null when it is not a quiz type. See <see cref="CanonicalValues"/>.
        /// </summary>
        public static string? Normalize(string? quizType) => CanonicalValues.Match(All, quizType);
    }
}
