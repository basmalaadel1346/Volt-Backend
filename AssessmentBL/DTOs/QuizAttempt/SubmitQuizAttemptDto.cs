namespace AssessmentBL.DTOs.QuizAttempt
{
    public class SubmitQuizAttemptDto
    {
        /// <summary>
        /// The answer to EVERY MultipleChoice and TrueFalse question in the attempt.
        /// The backend grades each against the frozen answer key and records only
        /// the wrong ones — which is why this list used to be called "mistakes",
        /// a name that described the backend's storage rather than what the client
        /// sends. A missing answer is rejected with 400; it used to be counted as
        /// correct, so an empty list scored 100%.
        /// </summary>
        public List<QuizAttemptAnswerDto> Answers { get; set; } = new();

        /// <summary>
        /// The answer to EVERY Essay question in the attempt — required like all
        /// other answers (400 otherwise). Empty only when the attempt has no essays.
        /// </summary>
        public List<QuizAttemptEssayAnswerDto> EssayAnswers { get; set; } = new();
    }
}
