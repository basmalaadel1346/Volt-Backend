namespace AssessmentBL.DTOs.Question
{
    public class UpdateQuestionDto
    {
        /// <summary>
        /// Optional. Null (or omitted) removes the question from its topic: PUT
        /// replaces the whole question. A new value must be an existing topic.
        /// </summary>
        public int? TopicId { get; set; }

        public string QuestionText { get; set; } = null!;

        /// <summary>MultipleChoice | TrueFalse | Essay. Blank keeps the current type.</summary>
        public string? QuestionType { get; set; }

        public string? ImageUrl { get; set; }

        /// <summary>
        /// Admin-only semantic description of the image, for the AI, which never
        /// looks at the image itself. REQUIRED (non-blank, at most 1000
        /// characters) when ImageUrl is supplied; cleared when the image is
        /// removed. NEVER returned to the child.
        /// </summary>
        public string? ImageDescription { get; set; }

        public string Difficulty { get; set; } = null!;

        public short DisplayOrder { get; set; }

        public byte Points { get; set; }

        public bool IsActive { get; set; }
    }
}
