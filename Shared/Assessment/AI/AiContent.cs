namespace Shared.Assessment.AI;

/// <summary>
/// The Assessment ↔ AI wire contract version. See docs/AI_CONTRACT.md.
/// 2: the essay result carries "points" (was "proposedPoints") and an optional
/// "reason"; "flags" is gone and "confidence" is optional — the AI's grade is
/// final once it is well-formed, with no review by a person.
/// </summary>
public static class AiContract
{
    public const string Version = "2";
}

/// <summary>Per-item status an AI response may carry.</summary>
public static class AiResultStatuses
{
    public const string Ok = "Ok";
    public const string Skipped = "Skipped";
}

/// <summary>
/// A question as the AI sees it. Text and image are separate so the model knows
/// which is which.
/// </summary>
public sealed class AiQuestion
{
    /// <summary>The question text the child saw, in the request language.</summary>
    public string? Text { get; init; }

    public AiImage? Image { get; init; }
}

public sealed class AiOption
{
    public int OptionId { get; init; }

    /// <summary>The option text the child saw. Null for an image-only option.</summary>
    public string? Text { get; init; }

    public AiImage? Image { get; init; }
}

/// <summary>
/// An image reaches the AI as its admin-authored description, and as its bytes
/// only when the backend is configured for a vision-capable model. The stored
/// ImageUrl is never sent — it is a server-relative path the AI cannot reach.
/// </summary>
public sealed class AiImage
{
    public string? Description { get; init; }

    public AiImageContent? Content { get; init; }
}

public sealed class AiImageContent
{
    /// <summary>image/png | image/jpeg | image/webp.</summary>
    public string MediaType { get; init; } = string.Empty;

    public string Base64 { get; init; } = string.Empty;
}
