using Microsoft.EntityFrameworkCore;
using UsersDA.Interfaces;
using UsersDA.Context;
using UsersDA.Entities;

namespace UsersDA.Repositories;

public class UserRepository : IUserRepository
{
    private readonly UsersDbContext _context;
    public UserRepository(UsersDbContext context) => _context = context;

    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _context.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        _context.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

    public Task<bool> EmailExistsAsync(string email, CancellationToken ct = default) =>
        _context.Users.AnyAsync(u => u.Email == email, ct);

    public Task<User?> GetByProviderAsync(string provider, string providerUserId, CancellationToken ct = default) =>
        _context.Users.FirstOrDefaultAsync(u => u.AuthProvider == provider && u.ProviderUserId == providerUserId, ct);

    public async Task AddAsync(User user, CancellationToken ct = default) =>
        await _context.Users.AddAsync(user, ct);
}
