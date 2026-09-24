using AssessmentBL.DTOs.QuestionOption;
namespace AssessmentBL.DTOs.Question
{
    public class AdminQuestionResponseDto
    {
        public int Id { get; set; }

        public int QuizId { get; set; }

        /// <summary>Null when the question belongs to no topic.</summary>
        public int? TopicId { get; set; }

        public string QuestionText { get; set; } = null!;

        public string QuestionType { get; set; } = null!;

        public string? ImageUrl { get; set; }

        /// <summary>
        /// Admin-only semantic description of the image, for the AI. NEVER
        /// returned to the child.
        /// </summary>
        public string? ImageDescription { get; set; }

        public string Difficulty { get; set; } = null!;

        public short DisplayOrder { get; set; }

        public byte Points { get; set; }

        /// <summary>
        /// Essay questions only: the ideas a good answer mentions, separated by
        /// commas or new lines (up to 1000 characters).
        ///
        /// They are the FALLBACK grader, not the grader. An essay is graded by the
        /// AI; these are used only when the AI never manages to — it is down, or
        /// every attempt failed, or the grading deadline passed. Without them such
        /// an essay closes as NotGraded and earns nothing, which is a worse answer
        /// to "the AI is down" than a rough but honest keyword score.
        ///
        /// Ignored (and stored as null) for any other question type. NEVER returned
        /// to the child.
        /// </summary>
        public string? EssayKeywords { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; }

        public IReadOnlyList<AdminQuestionOptionResponseDto> Options { get; set; }
            = [];
    }
}
