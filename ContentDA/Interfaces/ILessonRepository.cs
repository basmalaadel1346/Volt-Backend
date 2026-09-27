using ContentDA.Entities;

namespace ContentDA.Interfaces;

public interface ILessonRepository
{
    Task<List<Lesson>> GetByLevelIdAsync(int levelId, CancellationToken ct = default);
    Task<Lesson?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>الدرس مع كل عناصر المحتوى بتاعته (Include)، مرتبة حسب SortOrder.</summary>
    Task<Lesson?> GetWithContentsAsync(int id, CancellationToken ct = default);

    /// <summary>أعلى SortOrder داخل المستوى ده (0 لو مفيش دروس خالص فيه).</summary>
    Task<int> GetMaxSortOrderAsync(int levelId, CancellationToken ct = default);

    /// <summary>كل الدروس المنشورة بس، مرتبة بترتيب المسار الكامل (Level.Order ثم Lesson.SortOrder)،
    /// مع النوع بتاعها (LessonType) - مستخدمة في شاشة الهوم بتاعت الفلاتر.</summary>
    Task<List<Lesson>> GetAllPublishedOrderedAsync(CancellationToken ct = default);

    Task AddAsync(Lesson lesson, CancellationToken ct = default);
    void Remove(Lesson lesson);
}
