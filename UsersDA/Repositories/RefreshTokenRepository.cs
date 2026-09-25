using Microsoft.EntityFrameworkCore;
using UsersDA.Interfaces;
using UsersDA.Context;
using UsersDA.Entities;

namespace UsersDA.Repositories;

public class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly UsersDbContext _context;
    public RefreshTokenRepository(UsersDbContext context) => _context = context;

    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default) =>
        _context.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == tokenHash, ct);

    public async Task AddAsync(RefreshToken token, CancellationToken ct = default) =>
        await _context.RefreshTokens.AddAsync(token, ct);
}
