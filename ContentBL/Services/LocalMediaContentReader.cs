using Microsoft.AspNetCore.Hosting;
using Shared.Content;

namespace ContentBL.Services;

// [Assessment-AI] Implements the Shared contract IMediaContentReader so Assessment
// can send image bytes to a vision-capable AI (Assessment:AiSendImageContent).
// Content owns where uploads live, so the read belongs here. Read-only, uploads
// folder only; no Content table, entity or upload path was changed.
//
// بتقرا صورة مرفوعة (تحت /uploads/lessons بس) عشان تتبعت لموديل ذكاء اصطناعي بيشوف الصور.
// أي مسار تاني أو "../" بيترفض - مابنقراش أي ملف برّه فولدر الرفع.
public class LocalMediaContentReader : IMediaContentReader
{
    private static readonly Dictionary<string, string> MediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp"
    };

    private readonly IWebHostEnvironment _environment;

    public LocalMediaContentReader(IWebHostEnvironment environment) => _environment = environment;

    public async Task<MediaContent?> ReadImageAsync(
        string serverRelativeUrl,
        long maxBytes,
        CancellationToken cancellationToken = default)
    {
        var prefix = $"/{LocalImageStorageService.UploadsFolder}/";

        if (string.IsNullOrWhiteSpace(serverRelativeUrl)
            || !serverRelativeUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var webRootPath = _environment.WebRootPath;
        if (webRootPath is null)
            return null;

        var uploadsRoot = Path.GetFullPath(Path.Combine(webRootPath, LocalImageStorageService.UploadsFolder));
        var fullPath = Path.GetFullPath(Path.Combine(
            webRootPath, serverRelativeUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));

        // Whatever the stored URL says, never read outside the uploads folder.
        if (!fullPath.StartsWith(uploadsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return null;

        if (!MediaTypes.TryGetValue(Path.GetExtension(fullPath), out var mediaType))
            return null;

        var file = new FileInfo(fullPath);
        if (!file.Exists || file.Length > maxBytes)
            return null;

        return new MediaContent(mediaType, await File.ReadAllBytesAsync(fullPath, cancellationToken));
    }
}
