using AssessmentBL.DTOs.Placement;

namespace AssessmentBL.DTOs.QuizAttempt
{
    public class QuizAttemptResultDto
    {
        public long AttemptId { get; set; }

        /// <summary>Lets a client that recovered this result start a retry
        /// (POST /api/quiz-attempts?quizId=…&amp;previousAttemptId=…).</summary>
        public int QuizId { get; set; }

        /// <summary>When the result was saved (UTC).</summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>Every question in the attempt, essays included.</summary>
        public short TotalQuestions { get; set; }

        /// <summary>
        /// Questions the backend could score by itself: total minus essays.
        /// Their Points are the denominator of ScorePercentage.
        /// </summary>
        public short AutoGradedQuestions { get; set; }

        /// <summary>
        /// Essay answers the AI has not finished with yet. Essays are never part of
        /// ScorePercentage; their grades are in EssayResults.
        /// </summary>
        public short PendingEssayQuestions { get; set; }

        /// <summary>Every essay answer of the attempt, in display order.</summary>
        public List<EssayResultDto> EssayResults { get; set; } = new();

        public short CorrectAnswers { get; set; }

        public short WrongAnswers { get; set; }

        /// <summary>
        /// Points of the correct MultipleChoice/TrueFalse answers ÷ Points of all
        /// MultipleChoice/TrueFalse questions × 100, 2dp. Each question weighs its
        /// Points (1 unless the admin set another value), frozen when the attempt
        /// started. Essays are never part of it: this is final at submit, while
        /// essays are graded by the AI afterwards. 0 when nothing was auto-graded.
        /// </summary>
        public decimal ScorePercentage { get; set; }

        /// <summary>Sum of the Points of every question in the attempt, essays included.</summary>
        public int TotalPoints { get; set; }

        /// <summary>
        /// Points earned so far: the correct MultipleChoice/TrueFalse answers plus
        /// the AwardedPoints of Graded essays. Can still grow while
        /// PendingEssayQuestions is above 0; final once it is 0.
        /// </summary>
        public int EarnedPoints { get; set; }

        /// <summary>MaxPoints of the essays still Pending: points not decided yet.</summary>
        public int PendingPoints { get; set; }

        public string Language { get; set; } = null!;

        public bool LanguageFallbackApplied { get; set; }

        /// <summary>
        /// NotRequired | Generated | Partial | Unavailable. Whether the retry
        /// questions carry AI hints. Hints are optional — the score above is final
        /// whatever this says. When not Generated, CurrentHint is null on the
        /// retry questions that have no hint.
        /// </summary>
        public string HintsStatus { get; set; } = null!;

        public List<QuizQuestionForAttemptDto> RetryQuestions { get; set; } = new();

        /// <summary>
        /// Only for a placement attempt: the level the learner was placed at.
        /// Null for every other quiz. A placement attempt has no retry questions
        /// and no hints — it is not retried.
        /// </summary>
        public PlacementResultDto? Placement { get; set; }
    }
}
