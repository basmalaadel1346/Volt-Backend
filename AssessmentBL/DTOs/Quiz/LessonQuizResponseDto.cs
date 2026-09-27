namespace AssessmentBL.DTOs.Quiz
{
    /// <summary>
    /// Child-facing summary of the quiz attached to a lesson: enough to draw the
    /// "start the quiz" card, and nothing more.
    ///
    /// It deliberately carries NO questions. The questions of a quiz are served
    /// exactly once, by POST /api/quiz-attempts, because only that response is the
    /// frozen set the submission is graded against. Returning a second, unfrozen
    /// copy here invited the app to render one set and submit against another, and
    /// doubled the payload of a screen that only needs a title and a count.
    /// </summary>
    public class LessonQuizResponseDto
    {
        public int QuizId { get; set; }

        public int LessonId { get; set; }

        public string Title { get; set; } = null!;

        public string? Description { get; set; }

        /// <summary>Active questions only — the ones an attempt would contain.</summary>
        public short TotalQuestions { get; set; }

        /// <summary>Sum of those questions' Points: what a perfect attempt is worth.</summary>
        public int TotalPoints { get; set; }

        /// <summary>The language this response was resolved in ("en" | "ar").</summary>
        public string Language { get; set; } = null!;

        public bool LanguageFallbackApplied { get; set; }
    }

    /// <summary>
    /// Which quiz to start for a level, by level id. Answers the app's question
    /// "the child is on level 3 — which quiz do I open?" without it having to page
    /// through the admin quiz list and guess.
    /// </summary>
    public class LevelQuizResponseDto
    {
        public int LevelId { get; set; }

        /// <summary>The level's active LevelAssessment quiz.</summary>
        public int QuizId { get; set; }

        public string Title { get; set; } = null!;

        public string? Description { get; set; }

        public short TotalQuestions { get; set; }

        public int TotalPoints { get; set; }

        public string Language { get; set; } = null!;

        public bool LanguageFallbackApplied { get; set; }
    }
}
