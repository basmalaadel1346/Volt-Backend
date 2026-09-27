using System;
using System.Collections.Generic;

namespace AssessmentDA.Entities;

public partial class QuestionOption
{
    public int Id { get; set; }

    public int QuestionId { get; set; }

    /// <summary>Null when the option is image-only. CK_QuestionOptions_TextOrImage
    /// guarantees at least one of OptionText / ImageUrl is present.</summary>
    public string? OptionText { get; set; }

    /// <summary>Optional image. Server-relative path.</summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Admin-authored semantic description of <see cref="ImageUrl"/>. The AI
    /// never looks at images, so without it the AI cannot tell what the child
    /// picked. CK_QuestionOptions_ImageHasDescription requires it whenever
    /// ImageUrl is set — even when <see cref="OptionText"/> is also present,
    /// because the text may only label the picture. NULL when there is no
    /// image. NEVER returned in a child-facing response.
    /// </summary>
    public string? ImageDescription { get; set; }

    public bool IsCorrect { get; set; }

    public short DisplayOrder { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Question Question { get; set; } = null!;

    public virtual ICollection<QuizAttemptMistake> QuizAttemptMistakes { get; set; } = new List<QuizAttemptMistake>();

    public virtual ICollection<QuestionOptionTranslation> QuestionOptionTranslations { get; set; }
        = new List<QuestionOptionTranslation>();
}
