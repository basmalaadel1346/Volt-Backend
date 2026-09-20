using System;
using System.Collections.Generic;

namespace AssessmentDA.Entities;

/// <summary>
/// A child's free-text answer to an Essay question. Separate from
/// QuizAttemptMistake because it has a grading lifecycle and does not mean
/// "this answer was wrong", and separate from QuizAttemptQuestion because that
/// row is written once at attempt start and never modified.
/// Essays are graded by the AI only.
/// </summary>
public partial class QuizAttemptEssayAnswer
{
    public long Id { get; set; }

    public long QuizAttemptId { get; set; }

    public int QuestionId { get; set; }

    public string AnswerText { get; set; } = null!;

    /// <summary>Pending | Graded | NotGraded. Only Pending is not final.</summary>
    public string Status { get; set; } = null!;

    /// <summary>Only when Graded.</summary>
    public byte? AwardedPoints { get; set; }

    /// <summary>The AI's feedback for the child. Only when Graded.</summary>
    public string? Feedback { get; set; }

    /// <summary>"Ai" when Graded, otherwise null. The AI is the only grader.</summary>
    public string? GradedBy { get; set; }

    public DateTime? GradedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>The language the child answered in; AI feedback is written in it.</summary>
    public string LanguageCode { get; set; } = null!;

    /// <summary>
    /// Copied at submit from QuizAttemptQuestions.Points, i.e. the question's
    /// Points frozen at attempt start — the grade's ceiling
    /// (CK_QuizAttemptEssayAnswers_AwardedWithinMax). Frozen so a later edit of
    /// the question cannot put a grade above the maximum shown.
    /// </summary>
    public byte MaxPoints { get; set; }

    // ---- AI evaluation: processing state. The grade itself is Status /
    // ---- AwardedPoints / Feedback above.

    /// <summary>
    /// How the evaluation ended: Accepted (Graded) | Declined | Failed (both
    /// NotGraded). Null exactly while Pending (CK_QuizAttemptEssayAnswers_OutcomeMatchesStatus).
    /// </summary>
    public string? AiOutcome { get; set; }

    public byte AiEvaluationAttempts { get; set; }

    public DateTime? AiLastAttemptAt { get; set; }

    /// <summary>
    /// The confidence the AI reported with its grade, when it sent one.
    /// For monitoring only: it never decides whether a grade is accepted.
    /// </summary>
    public decimal? AiConfidence { get; set; }

    /// <summary>
    /// Which evaluation run owns the answer right now. Set atomically when a run
    /// claims it, so two app instances never evaluate the same answer at once.
    /// </summary>
    public Guid? AiClaimId { get; set; }

    public virtual QuizAttempt QuizAttempt { get; set; } = null!;

    public virtual Question Question { get; set; } = null!;
}
