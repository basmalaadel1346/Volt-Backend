using ContentDA.Context;
using ContentDA.Entities;
using ContentDA.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ContentDA.Repositories;

public class LevelRepository : ILevelRepository
{
    private readonly ContentDbContext _context;
    public LevelRepository(ContentDbContext context) => _context = context;

    public Task<List<Level>> GetAllAsync(CancellationToken ct = default) =>
        _context.Levels.OrderBy(l => l.Order).ToListAsync(ct);

    public Task<Level?> GetByIdAsync(int id, CancellationToken ct = default) =>
        _context.Levels.FirstOrDefaultAsync(l => l.Id == id, ct);

    public async Task<int> GetMaxOrderAsync(CancellationToken ct = default) =>
        await _context.Levels.AnyAsync(ct) ? await _context.Levels.MaxAsync(l => l.Order, ct) : 0;

    public async Task AddAsync(Level level, CancellationToken ct = default) =>
        await _context.Levels.AddAsync(level, ct);

    public void Remove(Level level) => _context.Levels.Remove(level);
}
