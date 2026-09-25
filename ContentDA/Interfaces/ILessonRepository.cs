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

    Task AddAsync(Lesson lesson, CancellationToken ct = default);
    void Remove(Lesson lesson);

    /// <summary>الدروس المنشورة بس (IsPublished = true) مع الـ Level بتاعها (Include)،
    /// مرتبة حسب ترتيب المستوى ثم ترتيب الدرس جوه المستوى - للـ Home Feed.</summary>
    Task<List<Lesson>> GetPublishedAsync(CancellationToken ct = default);
}
