using AssessmentBL.Services.Constants;

namespace AssessmentBL.Services
{
    /// <summary>A translation as the admin sent it, before any check.</summary>
    internal sealed record TranslationInput(string? LanguageCode, string? Name, string? Description);

    /// <summary>A translation that passed <see cref="TaxonomyInput.Translations"/>.</summary>
    internal sealed record CheckedTranslation(string LanguageCode, string Name, string? Description);

    /// <summary>
    /// The input rules categories and topics share, so both answer a bad name or
    /// translation with the same readable 400 instead of a SQL error.
    /// </summary>
    internal static class TaxonomyInput
    {
        public static string RequiredName(string? value, int maxLength, string label)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"{label} مطلوب", "name");

            var trimmed = value.Trim();
            if (trimmed.Length > maxLength)
                throw new ArgumentException($"{label} لا يتجاوز {maxLength} حرف", "name");

            return trimmed;
        }

        public static string? OptionalText(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        /// <summary>
        /// Each translation in a supported language ("ar" | "en", case-insensitive),
        /// at most once per language, with a name that fits the column.
        /// </summary>
        public static List<CheckedTranslation> Translations(IEnumerable<TranslationInput?> items, int maxNameLength)
        {
            var result = new List<CheckedTranslation>();

            foreach (var item in items)
            {
                if (item is null)
                    throw new ArgumentException("قائمة الترجمات تحتوي على عنصر فارغ", "translations");

                var code = item.LanguageCode?.Trim().ToLowerInvariant();

                if (string.IsNullOrEmpty(code) || !ContentLanguages.Supported.Contains(code))
                    throw new ArgumentException(
                        $"لغة الترجمة '{item.LanguageCode}' غير مدعومة، اللغات المتاحة: {string.Join(", ", ContentLanguages.Supported)}",
                        "translations");

                if (result.Any(t => t.LanguageCode == code))
                    throw new ArgumentException($"الترجمة بلغة '{code}' مكررة", "translations");

                result.Add(new CheckedTranslation(
                    code,
                    RequiredName(item.Name, maxNameLength, $"اسم الترجمة بلغة '{code}'"),
                    OptionalText(item.Description)));
            }

            return result;
        }
    }
}
