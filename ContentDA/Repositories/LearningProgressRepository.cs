using ContentDA.Context;
using ContentDA.Entities;
using ContentDA.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ContentDA.Repositories;

public class LearningProgressRepository : ILearningProgressRepository
{
    private readonly ContentDbContext _context;
    public LearningProgressRepository(ContentDbContext context) => _context = context;

    public Task<List<int>> GetCompletedLessonIdsAsync(Guid userId, CancellationToken ct = default) =>
        _context.LearningProgresses.Where(p => p.UserId == userId).Select(p => p.LessonId).ToListAsync(ct);

    public Task<LearningProgress?> GetByUserAndLessonAsync(Guid userId, int lessonId, CancellationToken ct = default) =>
        _context.LearningProgresses.FirstOrDefaultAsync(p => p.UserId == userId && p.LessonId == lessonId, ct);

    public async Task AddAsync(LearningProgress progress, CancellationToken ct = default) =>
        await _context.LearningProgresses.AddAsync(progress, ct);
}
