namespace ContentDA.Entities;

// ملحوظة: مفيش IsCompleted هنا عمدًا - وجود الصف نفسه (User + Lesson) معناه إن الدرس
// خلص، مفيش حالة "لسه شغال" بتتسجل خالص (الطفل لو خرج من الدرس بيرجع من الأول).
public partial class LearningProgress
{
    public int Id { get; set; }

    public Guid UserId { get; set; }

    public int LessonId { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Lesson Lesson { get; set; } = null!;
}
