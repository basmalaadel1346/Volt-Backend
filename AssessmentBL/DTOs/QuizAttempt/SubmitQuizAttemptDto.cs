using System.Collections.Generic;

namespace AssessmentBL.DTOs.QuizAttempt
{
    public class SubmitQuizAttemptDto
    {
        /// <summary>
        /// The answer to EVERY MultipleChoice and TrueFalse question in the attempt
        /// (the name is historical). The backend grades each against the frozen
        /// answer key and records only the wrong ones. A missing answer is rejected
        /// with 400 — it used to be counted as correct, so an empty list scored 100%.
        /// </summary>
        public List<QuizAttemptMistakeDto> Mistakes { get; set; } = new();

        /// <summary>
        /// The answer to EVERY Essay question in the attempt — required like all
        /// other answers (400 otherwise). Empty only when the attempt has no essays.
        /// </summary>
        public List<QuizAttemptEssayAnswerDto> EssayAnswers { get; set; } = new();
    }
}
