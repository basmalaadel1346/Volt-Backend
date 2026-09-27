using ContentDA.Context;
using ContentDA.Entities;
using ContentDA.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ContentDA.Repositories;

public class LessonContentRepository : ILessonContentRepository
{
    private readonly ContentDbContext _context;
    public LessonContentRepository(ContentDbContext context) => _context = context;

    public Task<List<LessonContent>> GetByLessonIdAsync(int lessonId, CancellationToken ct = default) =>
        _context.LessonContents.Where(c => c.LessonId == lessonId).OrderBy(c => c.SortOrder).ToListAsync(ct);

    public Task<LessonContent?> GetByIdAsync(int id, CancellationToken ct = default) =>
        _context.LessonContents.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<int> GetMaxSortOrderAsync(int lessonId, CancellationToken ct = default)
    {
        var query = _context.LessonContents.Where(c => c.LessonId == lessonId);
        return await query.AnyAsync(ct) ? await query.MaxAsync(c => c.SortOrder, ct) : 0;
    }

    public async Task AddAsync(LessonContent content, CancellationToken ct = default) =>
        await _context.LessonContents.AddAsync(content, ct);

    public void Remove(LessonContent content) => _context.LessonContents.Remove(content);

    public void RemoveRange(IEnumerable<LessonContent> contents) => _context.LessonContents.RemoveRange(contents);
}
