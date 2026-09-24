using AssessmentBL.DTOs.Placement;
using Shared.Gamification;

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
        /// NotRequired | Pending | Generated | Partial | Unavailable. Whether the
        /// hints for the wrong answers have been written yet. A submission returns
        /// Pending: the score below is final and committed, and the AI writes the
        /// hints afterwards. Hints are optional — the score is final whatever this
        /// ends up saying.
        ///
        /// The retry questions themselves are NOT in this response. Fetch them with
        /// GET /api/quiz-attempts/{attemptId}/retry-questions, once the hints-ready
        /// push arrives on the learner hub or whenever the child taps "try again".
        /// </summary>
        public string HintsStatus { get; set; } = null!;

        /// <summary>
        /// Only for a placement attempt: the level the learner was placed at, the
        /// full per-level breakdown, and how many lessons the placement completed.
        /// Null for every other quiz. A placement attempt is never retried, so it
        /// has no retry questions and no hints.
        /// </summary>
        public PlacementResultDto? Placement { get; set; }

        /// <summary>
        /// Only for a level-skip challenge: whether the level was skipped, and the
        /// hearts and score behind that verdict. Null for every other quiz.
        /// </summary>
        public LevelSkipResultDto? LevelSkip { get; set; }

        /// <summary>
        /// Sparks earned, the streak after this attempt, and any freeze it spent —
        /// everything the app needs to animate the reward bar without a second
        /// call. All zeros when the Gamification module is not configured.
        /// </summary>
        public RewardOutcome Rewards { get; set; } = RewardOutcome.None;
    }
}
