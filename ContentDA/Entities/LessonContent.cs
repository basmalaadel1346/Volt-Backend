using System;
using System.Collections.Generic;

namespace ContentDA.Entities;

public partial class LessonContent
{
    public int Id { get; set; }

    public int LessonId { get; set; }

    public int ContentTypeId { get; set; }

    public string? Content { get; set; }

    public string? MediaUrl { get; set; }

    public int SortOrder { get; set; }

    public virtual ContentType ContentType { get; set; } = null!;

    public virtual Lesson Lesson { get; set; } = null!;
}
