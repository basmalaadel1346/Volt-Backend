namespace ContentDA.Entities;

public partial class LessonType
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public virtual ICollection<Lesson> Lessons { get; set; } = new List<Lesson>();
}
