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
        /// LevelAssessment quiz (see PlacementEngine). At most one is active.
        /// </summary>
        public const string Placement = "Placement";

        // مصفوفة بتحتوي على كل الأنواع عشان نستخدمها في الـ Validation
        // في سطر: QuizTypes.All.Contains(quizType)
        public static readonly string[] All =
        {
            LevelAssessment,
            LessonQuiz,
            LessonReview,
            Standalone,
            Placement
        };
    }
}