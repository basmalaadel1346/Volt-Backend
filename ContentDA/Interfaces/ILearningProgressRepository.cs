using ContentDA.Entities;

namespace ContentDA.Interfaces;

public interface ILearningProgressRepository
{
    /// <summary>كل الـ LessonId المكتملة لليوزر ده.</summary>
    Task<List<int>> GetCompletedLessonIdsAsync(Guid userId, CancellationToken ct = default);

    Task<LearningProgress?> GetByUserAndLessonAsync(Guid userId, int lessonId, CancellationToken ct = default);

    Task AddAsync(LearningProgress progress, CancellationToken ct = default);
}
