using ContentDA.Context;
using ContentDA.Interfaces;

namespace ContentDA;

public class UnitOfWork : IUnitOfWork
{
    private readonly ContentDbContext _context;
    public UnitOfWork(ContentDbContext context) => _context = context;
    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _context.SaveChangesAsync(ct);
}
