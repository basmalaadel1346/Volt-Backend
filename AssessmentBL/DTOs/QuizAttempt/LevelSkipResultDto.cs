namespace AssessmentBL.DTOs.QuizAttempt
{
    /// <summary>
    /// The verdict of a level-skip challenge. Present only on a LevelSkip attempt's
    /// result; null for every other quiz.
    /// </summary>
    public class LevelSkipResultDto
    {
        /// <summary>The level the challenge was for.</summary>
        public int LevelId { get; set; }

        /// <summary>
        /// The level is skipped: its lessons are marked complete and the child may
        /// move on. False means "keep learning" — the challenge can be taken again.
        /// </summary>
        public bool Passed { get; set; }

        /// <summary>Wrong answers the challenge allowed. Null when it was not played on hearts.</summary>
        public byte? HeartsAllowed { get; set; }

        /// <summary>Hearts the child finished with. 0 means the challenge was lost on hearts.</summary>
        public byte? HeartsRemaining { get; set; }

        public int WrongAnswers { get; set; }

        /// <summary>Points earned ÷ points asked × 100, 2dp — the same arithmetic as any attempt.</summary>
        public decimal ScorePercentage { get; set; }

        /// <summary>The threshold a run with hearts to spare still had to reach.</summary>
        public decimal PassPercentage { get; set; }

        /// <summary>
        /// Lessons of the level this pass marked complete. 0 when the challenge was
        /// failed, or when the child had already finished them.
        /// </summary>
        public int LessonsCompleted { get; set; }
    }
}
