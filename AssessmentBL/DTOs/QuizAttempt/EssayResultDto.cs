namespace AssessmentBL.DTOs.QuizAttempt
{
    /// <summary>
    /// The state of one essay answer, as the child may see it. Essays are graded
    /// by the AI only: there is no model answer and no human reviewer.
    /// </summary>
    public class EssayResultDto
    {
        public int QuestionId { get; set; }

        /// <summary>
        /// Pending | Graded | NotGraded.
        /// Pending = the AI has not finished yet; ask again later
        /// (GET /api/quiz-attempts/{id}/result). Graded and NotGraded are final.
        /// NotGraded = the AI declined or could not evaluate the answer, so it
        /// earns no points and has no feedback.
        /// </summary>
        public string Status { get; set; } = null!;

        /// <summary>Only when Graded.</summary>
        public byte? AwardedPoints { get; set; }

        /// <summary>The question's Points, frozen when the attempt started.</summary>
        public byte MaxPoints { get; set; }

        /// <summary>The AI's feedback for the child, in the attempt's language. Only when Graded.</summary>
        public string? Feedback { get; set; }
    }
}
