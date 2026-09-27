namespace AssessmentBL.DTOs.QuizAttempt
{
    public class QuizAttemptResponseDto
    {
        public long AttemptId { get; set; }

        public int QuizId { get; set; }

        public DateTime StartedAt { get; set; }

        /// <summary>
        /// True when this response resumed an attempt that was already open rather
        /// than creating one — a double-tapped "Start", or the app retrying after a
        /// lost response. The attempt, its questions and its frozen Points are
        /// exactly the ones the first call returned.
        /// </summary>
        public bool Resumed { get; set; }

        /// <summary>
        /// When the quiz is played against the clock, the moment the attempt stops
        /// being submittable. Null for an untimed quiz. See
        /// <see cref="TimeLimitSeconds"/> for the length of the window.
        /// </summary>
        public DateTime? ExpiresAt { get; set; }

        /// <summary>Seconds allowed for the whole attempt, or null when untimed.</summary>
        public int? TimeLimitSeconds { get; set; }

        /// <summary>
        /// Wrong answers the child may spend before the attempt is over, or null
        /// when the quiz is not played on hearts. Only the level-skip challenge
        /// uses this today.
        /// </summary>
        public byte? Hearts { get; set; }

        /// <summary>The language this response was resolved in ("en" | "ar").</summary>
        public string Language { get; set; } = null!;

        /// <summary>
        /// True when at least one text field fell back to another language because
        /// the requested translation was missing. Fallback is per field.
        /// </summary>
        public bool LanguageFallbackApplied { get; set; }

        /// <summary>
        /// On a retry: questions the child got wrong last time that are NOT in this
        /// attempt, because an admin deactivated them in the meantime. They used to
        /// be dropped silently, so the child was handed a shorter retry with no
        /// explanation. Empty on a first attempt.
        /// </summary>
        public List<int> RemovedQuestionIds { get; set; } = new();

        /// <summary>
        /// A sentence to show the child, in the response's language, when something
        /// about this attempt needs explaining — today, that a question they missed
        /// has been removed. Null when there is nothing to say.
        /// </summary>
        public string? Notice { get; set; }

        public List<QuizQuestionForAttemptDto> Questions { get; set; } = new();
    }
}
