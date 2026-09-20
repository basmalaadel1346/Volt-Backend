namespace AssessmentBL.DTOs.Question
{
    public class CreateQuestionDto
    {
        public int QuizId { get; set; }

        /// <summary>
        /// Optional. When set, the topic must exist (GET /api/assessment/topics).
        /// A question with no topic earns the child XP but counts toward no topic's
        /// statistics.
        /// </summary>
        public int? TopicId { get; set; }

        public string QuestionText { get; set; } = null!;

        /// <summary>MultipleChoice | TrueFalse | Essay. Blank defaults to MultipleChoice.</summary>
        public string? QuestionType { get; set; }

        /// <summary>Optional. Upload via POST /api/content/media/images first, then send the returned url.</summary>
        public string? ImageUrl { get; set; }

        /// <summary>
        /// Admin-only semantic description of the image, for the AI, which never
        /// looks at the image itself. REQUIRED (non-blank, at most 1000
        /// characters) when ImageUrl is supplied; ignored and stored as null when
        /// it is not. NEVER returned to the child.
        /// </summary>
        public string? ImageDescription { get; set; }

        public string Difficulty { get; set; } = null!;

        public short DisplayOrder { get; set; }

        public byte Points { get; set; }
    }
}
