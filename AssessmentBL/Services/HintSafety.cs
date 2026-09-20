using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AssessmentBL.Services
{
    /// <summary>
    /// Guards the one thing a hint must never do: give the answer away. A hint is
    /// shown before the retry, so a hint that names the correct option turns the
    /// retry into copying. The AI is told not to; this checks that it didn't.
    /// </summary>
    public static class HintSafety
    {
        private static readonly TimeSpan RegexBudget = TimeSpan.FromMilliseconds(100);

        /// <summary>
        /// MultipleChoice: true when <paramref name="hint"/> names the correct
        /// option as a whole word or phrase — also with the article ال on either
        /// side ("أوم" / "الأوم") and behind up to two fused prefixes (و ف ب ك ل:
        /// "وبالأوم", "للأوم"), while "صحيح" does not name "صح".
        ///
        /// TrueFalse (<paramref name="isBinaryChoice"/>): the options are generic
        /// words ("صح", "خطأ", "true") an honest hint uses naturally ("خطأ شائع…"),
        /// so only an explicit verdict counts: "الإجابة خطأ", "the answer is true" —
        /// or the negated other option, "الإجابة ليست صح" / "the answer is not
        /// true", which gives "خطأ" / "false" away just the same.
        ///
        /// Diacritics, tatweel, ى/ي, ة/ه and Arabic-Indic digits compare equal.
        /// </summary>
        public static bool RevealsAnswer(
            string hint,
            string? correctOptionText,
            bool isBinaryChoice = false,
            string? otherOptionText = null)
        {
            if (string.IsNullOrWhiteSpace(hint) || string.IsNullOrWhiteSpace(correctOptionText))
                return false;

            var answer = Normalize(correctOptionText);

            // A one-character option ("A", "٣") would match everywhere; it cannot be
            // checked meaningfully, so it is not.
            if (answer.Length < 2)
                return false;

            var other = string.IsNullOrWhiteSpace(otherOptionText) ? null : Normalize(otherOptionText);

            var pattern = isBinaryChoice ? VerdictPattern(answer, other) : MentionPattern(answer);

            try
            {
                return Regex.IsMatch(
                    Normalize(hint), pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexBudget);
            }
            catch (RegexMatchTimeoutException)
            {
                // Could not prove it safe — treat it as unsafe; the question just
                // gets no hint.
                return true;
            }
        }

        private static string MentionPattern(string answer)
        {
            // The article is optional on both sides.
            var core = answer.StartsWith("ال", StringComparison.Ordinal) && answer.Length > 3
                ? answer[2..]
                : answer;

            return $@"(?<![\p{{L}}\p{{N}}])[وفبكل]{{0,2}}(?:ال)?{Regex.Escape(core)}(?![\p{{L}}\p{{N}}])";
        }

        private static string VerdictPattern(string answer, string? other)
        {
            var verdict = other is { Length: >= 2 }
                ? $@"(?:{Regex.Escape(answer)}|(?:ليست|ليس|غير|مش|not|isn't)\s+{Regex.Escape(other)})"
                : Regex.Escape(answer);

            // Matched against normalized text: الإجابة → الاجابه, العبارة → العباره …
            return @"(?:(?:الاجابه|الجواب|العباره|الجمله)(?:\s+الصحيحه)?(?:\s+(?:هي|هو))?\s*[:：]?\s*"
                 + @"|\b(?:answer|statement)(?:\s+is)?\s*:?\s*)"
                 + verdict
                 + @"(?![\p{L}\p{N}])";
        }

        /// <summary>
        /// Catches what <see cref="RevealsAnswer"/> cannot: a hint that does not
        /// quote the answer but comes within <paramref name="threshold"/> of it —
        /// a changed letter, a misspelling, one word of a two-word answer. Compares
        /// the answer against every same-length run of words in the hint, word by
        /// word, by edit distance.
        ///
        /// Not applied to TrueFalse: its options are generic words, so any hint
        /// about them would score high.
        /// </summary>
        public static bool IsTooSimilar(string hint, string? correctOptionText, decimal threshold)
        {
            if (string.IsNullOrWhiteSpace(hint) || string.IsNullOrWhiteSpace(correctOptionText) || threshold > 1m)
                return false;

            var answerWords = Words(Normalize(correctOptionText));
            var hintWords = Words(Normalize(hint));

            if (answerWords.Length == 0 || hintWords.Length == 0)
                return false;

            if (answerWords.Length == 1)
                return hintWords.Any(word => Similarity(answerWords[0], word) >= threshold);

            // Multi-word answer: slide a window the length of the answer over the
            // hint and ask how much of the answer that window reproduces.
            for (var start = 0; start + answerWords.Length <= hintWords.Length; start++)
            {
                var window = hintWords.AsSpan(start, answerWords.Length);
                var matched = 0;

                foreach (var answerWord in answerWords)
                {
                    foreach (var windowWord in window)
                    {
                        if (Similarity(answerWord, windowWord) >= threshold)
                        {
                            matched++;
                            break;
                        }
                    }
                }

                if ((decimal)matched / answerWords.Length >= threshold)
                    return true;
            }

            return false;
        }

        private static string[] Words(string text) =>
            text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        /// <summary>1 = identical, 0 = nothing in common (normalized edit distance).</summary>
        private static decimal Similarity(string a, string b)
        {
            if (a == b)
                return 1m;

            var longest = Math.Max(a.Length, b.Length);

            // Long strings cannot be near-copies of a short answer word, and the
            // distance is not worth computing.
            if (longest == 0 || longest > 64 || Math.Abs(a.Length - b.Length) > longest / 2)
                return 0m;

            var distance = EditDistance(a, b);

            // On a short word a single edit already reads as the same word ("ohm"
            // and "ohmm"), which the ratio alone would let through: 1 - 1/4 = 0.75.
            if (distance <= 1 && Math.Min(a.Length, b.Length) >= 3)
                return 1m;

            return 1m - (decimal)distance / longest;
        }

        private static int EditDistance(string a, string b)
        {
            var previous = new int[b.Length + 1];
            var current = new int[b.Length + 1];

            for (var j = 0; j <= b.Length; j++)
                previous[j] = j;

            for (var i = 1; i <= a.Length; i++)
            {
                current[0] = i;

                for (var j = 1; j <= b.Length; j++)
                {
                    var substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                    current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), substitution);
                }

                (previous, current) = (current, previous);
            }

            return previous[b.Length];
        }

        internal static string Normalize(string text)
        {
            var decomposed = text.Trim().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);

            foreach (var ch in decomposed)
            {
                // Tashkeel, hamza/madda marks, Latin accents — and tatweel.
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark || ch == 'ـ')
                    continue;

                builder.Append(ch switch
                {
                    'ى' => 'ي',
                    'ة' => 'ه',
                    '’' => '\'',
                    >= '٠' and <= '٩' => (char)('0' + (ch - '٠')),   // Arabic-Indic digits
                    >= '۰' and <= '۹' => (char)('0' + (ch - '۰')),   // Extended (Persian) digits
                    _ => ch
                });
            }

            var recomposed = builder.ToString().Normalize(NormalizationForm.FormC);
            return Regex.Replace(recomposed, @"\s+", " ").ToLowerInvariant();
        }
    }
}
