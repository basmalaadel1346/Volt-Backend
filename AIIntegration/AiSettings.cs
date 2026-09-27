namespace AIIntegration;

public sealed class AiSettings
{
    public const string SectionName = "Ai";

    public string HintsEndpoint { get; set; } = string.Empty;

    public string EssayEvaluationEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Sent in <see cref="ApiKeyHeaderName"/> on every call when set. Keep it in
    /// user-secrets or an environment variable (Ai__ApiKey), never in appsettings.json.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    public string ApiKeyHeaderName { get; set; } = "X-Api-Key";
}
