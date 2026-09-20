using System;
using System.Collections.Generic;

namespace AssessmentDA.Entities;

public partial class Question
{
    public int Id { get; set; }

    public int QuizId { get; set; }

    /// <summary>
    /// Optional. A question with no topic is asked, graded and earns XP like any
    /// other; it simply has no topic for its answers to count toward in
    /// UserTopicStats.
    /// </summary>
    public int? TopicId { get; set; }

    public string QuestionText { get; set; } = null!;

    /// <summary>MultipleChoice | TrueFalse | Essay. Determines delivery and grading.</summary>
    public string QuestionType { get; set; } = null!;

    /// <summary>Optional illustration. Server-relative path, e.g. /uploads/lessons/x.png.</summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Admin-authored semantic description of <see cref="ImageUrl"/>. The AI
    /// never looks at images, so this is the only way it learns what the image
    /// shows. CK_Questions_ImageHasDescription requires it whenever ImageUrl is
    /// set; it is NULL when there is no image, so a description never outlives
    /// a removed image. NEVER returned in a child-facing response.
    /// </summary>
    public string? ImageDescription { get; set; }

    public string Difficulty { get; set; } = null!;

    public short DisplayOrder { get; set; }

    public byte Points { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<QuestionOption> QuestionOptions { get; set; }
        = new List<QuestionOption>();
    public virtual Quiz Quiz { get; set; } = null!;

    public virtual ICollection<QuizAttemptMistake> QuizAttemptMistakes { get; set; } = new List<QuizAttemptMistake>();

    public virtual Topic? Topic { get; set; }
    public virtual ICollection<QuizAttemptQuestion> QuizAttemptQuestions { get; set; }
    = new List<QuizAttemptQuestion>();

    public virtual ICollection<QuestionTranslation> QuestionTranslations { get; set; }
        = new List<QuestionTranslation>();

    public virtual ICollection<QuizAttemptEssayAnswer> QuizAttemptEssayAnswers { get; set; }
        = new List<QuizAttemptEssayAnswer>();
}
