using System;
using System.Collections.Generic;

namespace ContentDA.Entities;

public partial class Lesson
{
    public int Id { get; set; }

    public int LevelId { get; set; }

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    public int SortOrder { get; set; }

    public bool IsPublished { get; set; }

    // "lesson" أو "finalLevelQuiz" - شوفي ContentBL.DTOs.LessonTypes للقيم المسموحة.
    public string LessonType { get; set; } = "lesson";

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<LessonContent> LessonContents { get; set; } = new List<LessonContent>();

    public virtual ICollection<LearningProgress> LearningProgresses { get; set; } = new List<LearningProgress>();

    public virtual Level Level { get; set; } = null!;
}
