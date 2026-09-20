using System;
using System.Collections.Generic;

namespace AssessmentDA.Entities;

public partial class QuestionHint
{
    public long Id { get; set; }

    /// <summary>
    /// The attempt and question this hint belongs to. Carried here rather than
    /// only through the mistake, because a Hint-button hint exists while the
    /// attempt is still in progress, before any mistake row.
    /// </summary>
    public long QuizAttemptId { get; set; }

    public int QuestionId { get; set; }

    /// <summary>
    /// The wrong answer this hint explains — set for a hint generated after a
    /// submission, null for a Hint-button hint asked for mid-attempt.
    /// </summary>
    public long? QuizAttemptMistakeId { get; set; }

    public string HintText { get; set; } = null!;

    /// <summary>Position in this (attempt, question, language) chain: 1, 2, …</summary>
    public byte HintSequence { get; set; }

    /// <summary>
    /// Hint-button escalation level: 1 = soft nudge, 2 = more direct. Null for a
    /// hint generated after a submission, which does not escalate.
    /// </summary>
    public byte? AttemptNumber { get; set; }

    /// <summary>The language this hint was GENERATED in — not a translation of
    /// another hint. Sequence numbering is per-language.</summary>
    public string LanguageCode { get; set; } = null!;

    public DateTime GeneratedAt { get; set; }

    public virtual QuizAttemptMistake? QuizAttemptMistake { get; set; }

    public virtual QuizAttemptQuestion QuizAttemptQuestion { get; set; } = null!;
}
