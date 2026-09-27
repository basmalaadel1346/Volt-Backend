using System.Globalization;
using System.Text;
using AssessmentBL.Services.Constants;

namespace AssessmentBL.Services
{
    /// <summary>
    /// The fallback grader for essay answers: match the ideas the admin listed for
    /// the question against what the child wrote.
    ///
    /// It is deliberately crude, and it is only ever reached once the AI has
    /// definitively not graded the answer. The alternative was leaving the child
    /// with NotGraded and zero points because a service they never heard of was
    /// down — an approximate grade they can see beats a perfect one that never
    /// arrives.
    ///
    /// Pure and side-effect free, so the rule is pinned by tests and the inline and
    /// background paths can never grade the same answer differently.
    /// </summary>
    public static class EssayKeywordGrading
    {
        /// <summary>
        /// Splits an admin's keyword field. Commas, Arabic commas, semicolons and
        /// new lines all separate; blanks and duplicates are dropped.
        /// </summary>
        public static IReadOnlyList<string> ParseKeywords(string? keywords)
        {
            if (string.IsNullOrWhiteSpace(keywords))
                return [];

            return keywords
                .Split([',', '،', ';', '؛', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim())
                .Where(k => k.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Grades <paramref name="answerText"/> against <paramref name="keywords"/>.
        /// Null when the question has no keywords — there is nothing to grade
        /// against, and inventing a score would be worse than admitting it.
        /// </summary>
        /// <param name="fullCreditPercentage">
        /// Share of the keywords that earns every point. Below it, points are
        /// proportional — a child who covered most of the answer is not treated
        /// like one who wrote nothing.
        /// </param>
        public static KeywordGrade? Grade(
            string? answerText,
            string? keywords,
            byte maxPoints,
            int fullCreditPercentage,
            string language)
        {
            var wanted = ParseKeywords(keywords);

            if (wanted.Count == 0)
                return null;

            var haystack = Normalize(answerText);

            var matched = wanted
                .Where(keyword => haystack.Contains(Normalize(keyword), StringComparison.Ordinal))
                .ToList();

            var coverage = matched.Count * 100m / wanted.Count;

            var awarded = coverage >= fullCreditPercentage
                ? maxPoints
                // Rounded DOWN, then floored at one point for any real match: the
                // fallback should never flatter a thin answer, and never wipe out a
                // child who did mention something.
                : (byte)Math.Max(
                    matched.Count == 0 ? 0 : 1,
                    (int)Math.Floor(maxPoints * coverage / 100m));

            return new KeywordGrade(
                awarded,
                matched.Count,
                wanted.Count,
                Feedback(matched.Count, wanted.Count, language));
        }

        /// <summary>
        /// Comparable text: lower-cased, Arabic diacritics and tatweel removed,
        /// alef/ya/ta-marbuta spellings unified, and whitespace collapsed — so
        /// "الأوم" matches "الاوم", which a child writing quickly will produce.
        /// </summary>
        private static string Normalize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var builder = new StringBuilder(text.Length);

            foreach (var ch in text.Normalize(NormalizationForm.FormKC))
            {
                switch (ch)
                {
                    // Arabic diacritics and the decorative tatweel carry no meaning.
                    case >= 'ً' and <= 'ْ':
                    case 'ـ':
                        continue;

                    case 'أ' or 'إ' or 'آ' or 'ٱ':
                        builder.Append('ا');
                        continue;

                    case 'ى':
                        builder.Append('ي');
                        continue;

                    case 'ة':
                        builder.Append('ه');
                        continue;
                }

                if (char.IsWhiteSpace(ch))
                {
                    if (builder.Length > 0 && builder[^1] != ' ')
                        builder.Append(' ');

                    continue;
                }

                builder.Append(char.ToLowerInvariant(ch));
            }

            return builder.ToString().Trim();
        }

        private static string Feedback(int matched, int total, string language)
        {
            var en = language == ContentLanguages.English;

            if (matched == 0)
                return en
                    ? "We could not check your answer with our helper this time, and it did not mention the key ideas. Have another look at the lesson!"
                    : "لم نتمكن من مراجعة إجابتك بالكامل هذه المرة، ولم تذكر الأفكار الأساسية. راجع الدرس مرة أخرى!";

            if (matched >= total)
                return en
                    ? "Great answer! You mentioned all the key ideas."
                    : "إجابة رائعة! ذكرت كل الأفكار الأساسية.";

            return en
                ? $"Good work! You mentioned {matched} of the {total} key ideas. Review the lesson to find the rest."
                : $"أحسنت! ذكرت {matched} من أصل {total} من الأفكار الأساسية. راجع الدرس لتعرف الباقي.";
        }
    }

    /// <param name="AwardedPoints">Points earned, never above the question's maximum.</param>
    /// <param name="MatchedKeywords">How many of the admin's keywords the answer mentioned.</param>
    /// <param name="TotalKeywords">How many the admin listed.</param>
    /// <param name="Feedback">The sentence shown to the child, in their language.</param>
    public sealed record KeywordGrade(
        byte AwardedPoints,
        int MatchedKeywords,
        int TotalKeywords,
        string Feedback);
}
