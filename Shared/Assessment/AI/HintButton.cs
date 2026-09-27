namespace Shared.Assessment.AI;

/// <summary>
/// One hint for one question, asked for by the child pressing the Hint button
/// (addendum v1.1 §D). Separate from <see cref="GenerateHintsRequest"/>, which
/// batches the wrong answers of a finished submission: this one is a single
/// question, on demand, and escalates.
///
/// Carries no user, attempt or account identifier — only the content of the
/// question and, optionally, the learner's age.
/// </summary>
public sealed class HintRequest
{
    public string ContractVersion { get; init; } = AiContract.Version;

    /// <summary>Random per call, to correlate logs. Never a database id.</summary>
    public Guid RequestId { get; init; }

    /// <summary>Always "Hint" — the batch task is "Hints".</summary>
    public string Task { get; init; } = AiTasks.Hint;

    /// <summary>
    /// 1 — a soft, indirect nudge: point at the concept or where to look, never
    /// confirm or restate that an earlier choice was wrong, never narrow it to
    /// "not X". 2 — closer and more direct: a specific wrong option may be
    /// eliminated, or the reasoning narrowed substantially. The correct answer is
    /// never named at either level; the backend rejects a hint that does.
    /// The server derives this; the client cannot set it.
    /// </summary>
    public int AttemptNumber { get; init; }

    public string Language { get; init; } = "ar";

    /// <summary>Omitted entirely when the learner's age is unknown.</summary>
    public LearnerContext? LearnerContext { get; init; }

    public HintQuestion Question { get; init; } = new();

    /// <summary>
    /// Hints already given for this question in this attempt, same language,
    /// oldest first. An addition to the payload in the addendum: without it a
    /// level-2 hint tends to repeat level 1. Do not repeat them — go further.
    /// </summary>
    public IReadOnlyList<string> PreviousHints { get; init; } = [];
}

public sealed class LearnerContext
{
    public int Age { get; init; }
}

public sealed class HintQuestion
{
    public string? Text { get; init; }

    /// <summary>MultipleChoice | TrueFalse | Essay.</summary>
    public string Type { get; init; } = string.Empty;

    public AiImage? Image { get; init; }

    /// <summary>Empty for Essay, which has no options.</summary>
    public IReadOnlyList<AiOption> Options { get; init; } = [];

    /// <summary>
    /// Null for Essay (there is nothing to compare against at hint time — grading
    /// is asynchronous). For MultipleChoice / TrueFalse the AI needs it to steer
    /// toward the answer without restating it.
    /// </summary>
    public HintCorrectAnswer? CorrectAnswer { get; init; }
}

public sealed class HintCorrectAnswer
{
    public int OptionId { get; init; }

    public string? Text { get; init; }
}

public sealed class HintResponse
{
    public string? ContractVersion { get; init; }

    public string? RequestId { get; init; }

    /// <summary>Ok | Skipped. A missing status is read as Ok.</summary>
    public string? Status { get; init; }

    /// <summary>In the request language. Must not reveal the correct option.</summary>
    public string? Hint { get; init; }

    /// <summary>Why it was skipped, for logs only.</summary>
    public string? Reason { get; init; }
}

public static class AiTasks
{
    /// <summary>Batch: hints for the wrong answers of a finished submission.</summary>
    public const string Hints = "Hints";

    /// <summary>Single question, on demand, escalating (the Hint button).</summary>
    public const string Hint = "Hint";

    /// <summary>Essay evaluation.</summary>
    public const string EssayEvaluation = "EssayEvaluation";
}

public interface IAiHintButton
{
    /// <summary>False while no AI endpoint is configured — callers skip the call.</summary>
    bool IsConfigured { get; }

    Task<HintResponse> RequestHintAsync(HintRequest request, CancellationToken cancellationToken = default);
}
