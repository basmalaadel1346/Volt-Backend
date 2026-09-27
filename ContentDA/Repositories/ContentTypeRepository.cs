using ContentDA.Context;
using ContentDA.Entities;
using ContentDA.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ContentDA.Repositories;

public class ContentTypeRepository : IContentTypeRepository
{
    private readonly ContentDbContext _context;
    public ContentTypeRepository(ContentDbContext context) => _context = context;

    public Task<List<ContentType>> GetAllAsync(CancellationToken ct = default) =>
        _context.ContentTypes.OrderBy(c => c.Id).ToListAsync(ct);

    public Task<bool> ExistsAsync(int id, CancellationToken ct = default) =>
        _context.ContentTypes.AnyAsync(c => c.Id == id, ct);
}
