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

    public async Task<bool> RevokeIfActiveAsync(Guid tokenId, DateTime now, CancellationToken ct = default)
    {
        // UPDATE ... WHERE RevokedAt IS NULL في أمر SQL واحد ذري (Atomic) - لو طلبين
        // بعتوا نفس اللحظة، الداتابيز نفسها بتضمن إن واحد بس ينجح (Row-level lock)، والتاني
        // هيرجعله 0 صف اتأثر بدل ما ياخد Exception أو يعمل توكنين لنفس الـ Refresh.
        var affected = await _context.RefreshTokens
            .Where(t => t.Id == tokenId && t.RevokedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, now), ct);

        return affected > 0;
    }

    public async Task RevokeAllActiveForUserAsync(Guid userId, DateTime now, CancellationToken ct = default)
    {
        await _context.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, now), ct);
    }

    // بيمسح أي صف منتهي أو ملغي من زمان (قبل olderThan) - Bulk DELETE في أمر SQL واحد (EF Core 8)
    // من غير ما يحمّل الصفوف في الذاكرة الأول
    public Task<int> DeleteOldExpiredOrRevokedAsync(DateTime olderThan, CancellationToken ct = default) =>
        _context.RefreshTokens
            .Where(t => t.ExpiresAt < olderThan || (t.RevokedAt != null && t.RevokedAt < olderThan))
            .ExecuteDeleteAsync(ct);
}
