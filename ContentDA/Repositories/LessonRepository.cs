using ContentDA.Context;
using ContentDA.Entities;
using ContentDA.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ContentDA.Repositories;

public class LessonRepository : ILessonRepository
{
    private readonly ContentDbContext _context;
    public LessonRepository(ContentDbContext context) => _context = context;

    public Task<List<Lesson>> GetByLevelIdAsync(int levelId, CancellationToken ct = default) =>
        _context.Lessons.Where(l => l.LevelId == levelId).OrderBy(l => l.SortOrder).ToListAsync(ct);

    public Task<Lesson?> GetByIdAsync(int id, CancellationToken ct = default) =>
        _context.Lessons.FirstOrDefaultAsync(l => l.Id == id, ct);

    public Task<Lesson?> GetWithContentsAsync(int id, CancellationToken ct = default) =>
        _context.Lessons
            .Include(l => l.LessonContents.OrderBy(c => c.SortOrder))
            .ThenInclude(c => c.ContentType)
            .FirstOrDefaultAsync(l => l.Id == id, ct);

    public async Task<int> GetMaxSortOrderAsync(int levelId, CancellationToken ct = default)
    {
        var query = _context.Lessons.Where(l => l.LevelId == levelId);
        return await query.AnyAsync(ct) ? await query.MaxAsync(l => l.SortOrder, ct) : 0;
    }

    public async Task AddAsync(Lesson lesson, CancellationToken ct = default) =>
        await _context.Lessons.AddAsync(lesson, ct);

    public void Remove(Lesson lesson) => _context.Lessons.Remove(lesson);

    public Task<List<Lesson>> GetPublishedAsync(CancellationToken ct = default) =>
        _context.Lessons
            .Include(l => l.Level)
            .Where(l => l.IsPublished)
            .OrderBy(l => l.Level.Order)
            .ThenBy(l => l.SortOrder)
            .ToListAsync(ct);
}
