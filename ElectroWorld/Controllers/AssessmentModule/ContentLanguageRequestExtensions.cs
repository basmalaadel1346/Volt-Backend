using AssessmentBL.Services.Constants;

namespace ElectroWorld.Api.Controllers;

internal static class ContentLanguageRequestExtensions
{
    /// <summary>
    /// Resolves the content language for this request: an explicit ?language=
    /// wins, otherwise the highest-weighted supported Accept-Language entry,
    /// otherwise the service default. Nothing here rejects a value — a bad
    /// language header must never fail a quiz request.
    /// </summary>
    public static string? ResolveContentLanguage(this HttpRequest request, string? language)
    {
        if (!string.IsNullOrWhiteSpace(language))
            return language;

        // Accept-Language is a weighted list ("en-US,en;q=0.9,ar;q=0.8"), not a
        // single tag. Passing it through raw made "en;q=0.9" or "en,ar-EG;q=0.8"
        // resolve to the default. An unparseable header yields an empty list.
        var preferred = request.GetTypedHeaders().AcceptLanguage
            .Where(l => (l.Quality ?? 1.0) > 0)
            .OrderByDescending(l => l.Quality ?? 1.0);

        foreach (var entry in preferred)
        {
            var tag = entry.Value.Value;
            if (string.IsNullOrWhiteSpace(tag))
                continue;

            var primary = tag.Split('-')[0].Trim().ToLowerInvariant();
            if (ContentLanguages.Supported.Contains(primary))
                return primary;
        }

        return null;
    }
}
