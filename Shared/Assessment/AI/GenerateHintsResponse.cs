namespace Shared.Assessment.AI;

public sealed class GenerateHintsResponse
{
    public string? ContractVersion { get; init; }

    public string? RequestId { get; init; }

    public IReadOnlyList<HintResult> Results { get; init; } = [];
}

public sealed class HintResult
{
    public int QuestionId { get; init; }

    /// <summary>Ok | Skipped. A missing status is read as Ok.</summary>
    public string? Status { get; init; }

    /// <summary>In the request language. Must not reveal the correct option.</summary>
    public string? Hint { get; init; }

    /// <summary>Why an item was skipped, for logs only.</summary>
    public string? Reason { get; init; }
}
