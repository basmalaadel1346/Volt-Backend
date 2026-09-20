using AssessmentBL.DTOs.QuizAttempt;
using AssessmentBL.Services.Constants;
using AssessmentDA.Entities;

namespace AssessmentBL.Services
{
    /// <summary>
    /// The child-facing, localized projection of questions and their options,
    /// shared by every endpoint that shows questions to a child: attempt start and
    /// resume, the retry set of a result, and the quiz-for-lesson lookup.
    /// Text resolves requested → fallback → base column, per field, and records
    /// whether the requested translation was missing so the response can say so.
    /// IsCorrect is never projected, so the answer key cannot leak through it.
    /// </summary>
    internal static class LocalizedQuestionQuery
    {
        public static IQueryable<LocalizedQuestionRow> Project(
            IQueryable<Question> questions, string language) =>
            questions
                .OrderBy(q => q.DisplayOrder)
                .Select(q => new LocalizedQuestionRow
                {
                    QuestionId = q.Id,
                    QuestionText =
                        q.QuestionTranslations
                            .Where(t => t.LanguageCode == language)
                            .Select(t => t.QuestionText)
                            .FirstOrDefault()
                        ?? q.QuestionTranslations
                            .Where(t => t.LanguageCode == ContentLanguages.Fallback)
                            .Select(t => t.QuestionText)
                            .FirstOrDefault()
                        ?? q.QuestionText,
                    HasRequestedTranslation =
                        q.QuestionTranslations.Any(t => t.LanguageCode == language),
                    QuestionType = q.QuestionType,
                    ImageUrl = q.ImageUrl,
                    // Admin-only metadata. Carried on the INTERNAL row so the AI
                    // can read it; deliberately not copied by ToDto().
                    ImageDescription = q.ImageDescription,
                    Difficulty = q.Difficulty,
                    DisplayOrder = q.DisplayOrder,
                    Points = q.Points,
                    CurrentHint = null,
                    // Essay questions simply have no option rows, so this comes back empty.
                    Options = q.QuestionOptions
                        .OrderBy(o => o.DisplayOrder)
                        .Select(o => new LocalizedOptionRow
                        {
                            OptionId = o.Id,
                            OptionText =
                                o.QuestionOptionTranslations
                                    .Where(t => t.LanguageCode == language)
                                    .Select(t => t.OptionText)
                                    .FirstOrDefault()
                                ?? o.QuestionOptionTranslations
                                    .Where(t => t.LanguageCode == ContentLanguages.Fallback)
                                    .Select(t => t.OptionText)
                                    .FirstOrDefault()
                                ?? o.OptionText,
                            ImageUrl = o.ImageUrl,
                            ImageDescription = o.ImageDescription,
                            DisplayOrder = o.DisplayOrder,
                            HasRequestedTranslation =
                                o.QuestionOptionTranslations.Any(t => t.LanguageCode == language)
                        })
                        .ToList()
                });
    }

    internal sealed class LocalizedQuestionRow
    {
        public int QuestionId { get; set; }
        public string QuestionText { get; set; } = null!;
        public bool HasRequestedTranslation { get; set; }
        public string QuestionType { get; set; } = null!;
        public string? ImageUrl { get; set; }
        /// <summary>Admin-only. Never copied into a child-facing DTO.</summary>
        public string? ImageDescription { get; set; }
        public string Difficulty { get; set; } = null!;
        public short DisplayOrder { get; set; }
        public byte Points { get; set; }
        public string? CurrentHint { get; set; }
        public List<LocalizedOptionRow> Options { get; set; } = [];

        public bool UsedFallback =>
            !HasRequestedTranslation || Options.Any(o => !o.HasRequestedTranslation);

        public QuizQuestionForAttemptDto ToDto() => new()
        {
            QuestionId = QuestionId,
            QuestionText = QuestionText,
            QuestionType = QuestionType,
            ImageUrl = ImageUrl,
            Difficulty = Difficulty,
            DisplayOrder = DisplayOrder,
            Points = Points,
            CurrentHint = CurrentHint,
            Options = Options
                .Select(o => new QuizAnswerOptionDto
                {
                    OptionId = o.OptionId,
                    OptionText = o.OptionText,
                    ImageUrl = o.ImageUrl,
                    DisplayOrder = o.DisplayOrder
                })
                .ToList()
        };
    }

    internal sealed class LocalizedOptionRow
    {
        public int OptionId { get; set; }
        public string? OptionText { get; set; }
        public string? ImageUrl { get; set; }
        /// <summary>Admin-only. Never copied into a child-facing DTO.</summary>
        public string? ImageDescription { get; set; }
        public short DisplayOrder { get; set; }
        public bool HasRequestedTranslation { get; set; }
    }
}
