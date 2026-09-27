namespace AssessmentBL.DTOs.QuizAttempt
{
    /// <summary>One answer to a MultipleChoice or TrueFalse question.</summary>
    public class QuizAttemptAnswerDto
    {
        public int QuestionId { get; set; }

        public int SelectedOptionId { get; set; }
    }
}
