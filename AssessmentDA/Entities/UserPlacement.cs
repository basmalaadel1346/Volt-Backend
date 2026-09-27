using System;
using System.Collections.Generic;

namespace AssessmentDA.Entities;

/// <summary>
/// The level a learner was placed at by the first-run placement test. One row
/// per user, written in the same transaction that completes the placement
/// attempt.
/// </summary>
public partial class UserPlacement
{
    public long Id { get; set; }

    public Guid UserId { get; set; }

    public long QuizAttemptId { get; set; }

    /// <summary>
    /// Loose reference to LearningContent.Levels — no FK, the same convention
    /// as Quizzes.LevelId.
    /// </summary>
    public int PlacedLevelId { get; set; }

    public decimal ScorePercentage { get; set; }

    /// <summary>
    /// The mastery threshold in force when the learner was placed, so the
    /// per-level breakdown always explains the stored level even if the setting
    /// changes later.
    /// </summary>
    public byte PassPercentage { get; set; }

    public DateTime PlacedAt { get; set; }

    public virtual QuizAttempt QuizAttempt { get; set; } = null!;
}
