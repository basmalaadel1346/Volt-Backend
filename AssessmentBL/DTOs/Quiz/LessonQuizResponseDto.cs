using AssessmentBL.DTOs.QuizAttempt;

namespace AssessmentBL.DTOs.Quiz
{
    /// <summary>
    /// Child-facing view of the quiz attached to a lesson. Carries no answer key
    /// and none of the admin metadata (IsActive, CreatedAt, image descriptions).
    /// </summary>
    public class LessonQuizResponseDto
    {
        public int QuizId { get; set; }

        public int LessonId { get; set; }

        public string Title { get; set; } = null!;

        public string? Description { get; set; }

        /// <summary>Active questions only — the ones an attempt would contain.</summary>
        public short TotalQuestions { get; set; }

        /// <summary>The language this response was resolved in ("en" | "ar").</summary>
        public string Language { get; set; } = null!;

        public bool LanguageFallbackApplied { get; set; }

        /// <summary>
        /// In DisplayOrder. Never contains IsCorrect. This is a preview: to answer,
        /// Flutter starts an attempt (POST /api/quiz-attempts?quizId=…), and the
        /// questions in THAT response are the frozen set the submission is graded
        /// against.
        /// </summary>
        public List<QuizQuestionForAttemptDto> Questions { get; set; } = new();
    }
}
