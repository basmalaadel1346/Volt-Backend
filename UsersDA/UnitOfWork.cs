using UsersDA.Interfaces;
using UsersDA.Context;

namespace UsersDA;

public class UnitOfWork : IUnitOfWork
{
    private readonly UsersDbContext _context;
    public UnitOfWork(UsersDbContext context) => _context = context;
    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _context.SaveChangesAsync(ct);
}
