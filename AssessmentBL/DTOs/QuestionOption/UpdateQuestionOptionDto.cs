namespace AssessmentBL.DTOs.QuestionOption
{
    public class UpdateQuestionOptionDto
    {
        public string? OptionText { get; set; }

        public string? ImageUrl { get; set; }

        /// <summary>
        /// Admin-only semantic description of the image, for the AI. REQUIRED
        /// (non-blank, at most 1000 characters) whenever ImageUrl is supplied,
        /// even alongside OptionText; cleared when the image is removed. NEVER
        /// returned to the child.
        /// </summary>
        public string? ImageDescription { get; set; }

        public bool IsCorrect { get; set; }

        public short DisplayOrder { get; set; }
    }
}
