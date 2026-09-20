namespace AssessmentBL.DTOs.QuizAttempt
{
    /// <summary>One press of the Hint button.</summary>
    public class HintResponseDto
    {
        public int QuestionId { get; set; }

        /// <summary>
        /// Which level this press was for: 1 = a soft nudge, 2 = a more direct hint.
        /// A press after the last level is refused with 409.
        /// </summary>
        public byte AttemptNumber { get; set; }

        /// <summary>Null when no hint could be produced — see HintsStatus.</summary>
        public string? Hint { get; set; }

        /// <summary>
        /// Generated — the hint is here and this level is used. Partial — the AI
        /// failed, timed out, or its hint was unusable or gave the answer away.
        /// Unavailable — the hint AI is not configured, or the question cannot be
        /// described to it. After Partial and Unavailable nothing was saved and no
        /// level was used: the press is not counted against the child.
        /// </summary>
        public string HintsStatus { get; set; } = null!;

        /// <summary>
        /// Presses still available for this question in this attempt, after this
        /// one. Unchanged by a Partial or Unavailable press. 0 means the button can
        /// be disabled.
        /// </summary>
        public int HintsRemaining { get; set; }

        public string Language { get; set; } = null!;
    }
}
