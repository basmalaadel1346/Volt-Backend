using ContentDA.Entities;

namespace ContentDA.Interfaces;

public interface ILessonContentRepository
{
    Task<List<LessonContent>> GetByLessonIdAsync(int lessonId, CancellationToken ct = default);
    Task<LessonContent?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>أعلى SortOrder داخل الدرس ده (0 لو مفيش عناصر محتوى خالص فيه).</summary>
    Task<int> GetMaxSortOrderAsync(int lessonId, CancellationToken ct = default);

    Task AddAsync(LessonContent content, CancellationToken ct = default);
    void Remove(LessonContent content);
    void RemoveRange(IEnumerable<LessonContent> contents);
}
