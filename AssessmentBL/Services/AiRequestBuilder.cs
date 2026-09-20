using AssessmentBL.Services.Constants;
using AssessmentDA.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Assessment.AI;
using Shared.Content;

namespace AssessmentBL.Services
{
    /// <summary>
    /// The pieces every Assessment → AI request shares: how an image is
    /// represented, the localized topic name, and the "can the AI interpret this"
    /// rules. Keeps the hint and essay requests consistent, and keeps what may
    /// leave the system in one place (docs/AI_CONTRACT.md).
    /// </summary>
    public sealed class AiRequestBuilder
    {
        private readonly AssessmentDbContext _db;
        private readonly IMediaContentReader _media;
        private readonly AssessmentSettings _settings;
        private readonly ILogger<AiRequestBuilder> _logger;

        public AiRequestBuilder(
            AssessmentDbContext db,
            IMediaContentReader media,
            IOptions<AssessmentSettings> settings,
            ILogger<AiRequestBuilder> logger)
        {
            _db = db;
            _media = media;
            _settings = settings.Value;
            _logger = logger;
        }

        /// <summary>One per AI request: caps the image bytes that request may carry.</summary>
        public AiImageBudget NewImageBudget() => new(_settings.EffectiveAiMaxImageBytesPerRequest);

        /// <summary>
        /// Null when there is neither an image nor a description. The admin-authored
        /// description is always included; the bytes only when
        /// <see cref="AssessmentSettings.AiSendImageContent"/> is on, the file is one
        /// of ours, and it fits both the per-image and the per-request limit. The
        /// server-relative URL itself is never sent.
        /// </summary>
        public async Task<AiImage?> BuildImageAsync(
            string? imageUrl,
            string? imageDescription,
            AiImageBudget budget,
            CancellationToken cancellationToken)
        {
            var description = Clean(imageDescription);

            if (string.IsNullOrWhiteSpace(imageUrl) && description is null)
                return null;

            AiImageContent? content = null;

            if (_settings.AiSendImageContent && !string.IsNullOrWhiteSpace(imageUrl) && budget.RemainingBytes > 0)
            {
                try
                {
                    var media = await _media.ReadImageAsync(
                        imageUrl, Math.Min(_settings.EffectiveAiMaxImageBytes, budget.RemainingBytes), cancellationToken);

                    if (media is not null && budget.TryTake(media.Bytes.LongLength))
                        content = new AiImageContent
                        {
                            MediaType = media.MediaType,
                            Base64 = Convert.ToBase64String(media.Bytes)
                        };
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // An unreadable file degrades to description-only; it never fails the item.
                    _logger.LogWarning(ex, "Could not read image {ImageUrl} for the AI; sending its description only.", imageUrl);
                }
            }

            return new AiImage { Description = description, Content = content };
        }

        /// <summary>
        /// Question id → localized topic name (requested → fallback → base). A
        /// question with no topic is absent, so its request carries "topic": null.
        /// </summary>
        public Task<Dictionary<int, string>> LoadTopicNamesAsync(
            List<int> questionIds,
            string language,
            CancellationToken cancellationToken) =>
            _db.Questions
                .AsNoTracking()
                .Where(q => questionIds.Contains(q.Id) && q.TopicId != null)
                .Select(q => new
                {
                    q.Id,
                    Name = q.Topic!.TopicTranslations
                            .Where(t => t.LanguageCode == language)
                            .Select(t => t.Name)
                            .FirstOrDefault()
                        ?? q.Topic!.TopicTranslations
                            .Where(t => t.LanguageCode == ContentLanguages.Fallback)
                            .Select(t => t.Name)
                            .FirstOrDefault()
                        ?? q.Topic!.Name
                })
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        public static string? Clean(string? text) =>
            string.IsNullOrWhiteSpace(text) ? null : text.Trim();

        /// <summary>
        /// The AI-visible meaning of a question. Text-only questions are unchanged.
        /// A question carried by an image contributes its admin-authored
        /// description; when both exist the description is appended, because a
        /// question image usually holds information the text refers to.
        /// Returns null when neither is available.
        /// </summary>
        public static string? QuestionSemanticText(string? questionText, string? imageDescription)
        {
            var text = Clean(questionText);
            var description = Clean(imageDescription);

            if (text is not null && description is not null)
                return $"{text}\n{description}";

            return text ?? description;
        }

        /// <summary>
        /// The AI-visible meaning of an option. Text wins; an image-only option
        /// contributes its description. Null when it has neither — the AI cannot
        /// interpret it, so the item must be skipped rather than sent empty.
        /// </summary>
        public static string? OptionSemanticText(string? optionText, string? imageDescription) =>
            Clean(optionText) ?? Clean(imageDescription);
    }

    /// <summary>The image bytes one AI request may still carry.</summary>
    public sealed class AiImageBudget
    {
        internal AiImageBudget(long bytes) => RemainingBytes = bytes;

        public long RemainingBytes { get; private set; }

        internal bool TryTake(long bytes)
        {
            if (bytes > RemainingBytes)
                return false;

            RemainingBytes -= bytes;
            return true;
        }
    }
}
