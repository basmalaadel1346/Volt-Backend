using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AssessmentDA.Entities;

public partial class UserTopicStat
{
    public long Id { get; set; }

    public Guid UserId { get; set; }

    public int TopicId { get; set; }

    public string Difficulty { get; set; } = null!;

    public int QuestionsAnsweredCount { get; set; }

    public int CorrectCount { get; set; }

    public int? WrongCount { get; set; }

    public int HintsUsedCount { get; set; }

    public long? LastQuizAttemptId { get; set; }

    public DateTime? LastPracticedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Makes the read-modify-write in UpdateAfterQuizAttemptAsync safe: two
    /// submissions updating the same row at once no longer lose one set of
    /// counts — the second gets a concurrency conflict and is retried.
    /// </summary>
    [Timestamp] public byte[] RowVersion { get; set; } = null!;

    public virtual QuizAttempt? LastQuizAttempt { get; set; }

    public virtual Topic Topic { get; set; } = null!;
}
