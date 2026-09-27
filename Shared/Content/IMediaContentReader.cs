namespace Shared.Content;

/// <summary>
/// Reads an uploaded image so its bytes can be sent to a vision-capable AI. The
/// Content module owns where files live; other modules only hold the
/// server-relative URL it returned.
/// </summary>
public interface IMediaContentReader
{
    /// <summary>
    /// Null when the URL is not one of our uploads, the file is missing, its
    /// type is not a supported image, or it is larger than <paramref name="maxBytes"/>.
    /// </summary>
    Task<MediaContent?> ReadImageAsync(
        string serverRelativeUrl,
        long maxBytes,
        CancellationToken cancellationToken = default);
}

public sealed record MediaContent(string MediaType, byte[] Bytes);
