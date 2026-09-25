using Microsoft.EntityFrameworkCore;
using UsersDA.Interfaces;
using UsersDA.Context;
using UsersDA.Entities;

namespace UsersDA.Repositories;

public class PasswordResetOtpRepository : IPasswordResetOtpRepository
{
    private readonly UsersDbContext _context;
    public PasswordResetOtpRepository(UsersDbContext context) => _context = context;

    public async Task AddAsync(PasswordResetOtp otp, CancellationToken ct = default) =>
        await _context.PasswordResetOtps.AddAsync(otp, ct);

    // أحدث OTP لسه صالح (مش متحقق منه، ومش منتهي) لليوزر ده
    public Task<PasswordResetOtp?> GetLatestUsableForUserAsync(Guid userId, CancellationToken ct = default) =>
        _context.PasswordResetOtps
            .Where(o => o.UserId == userId && o.VerifiedAt == null && o.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task DeleteAllUsableForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var oldOnes = await _context.PasswordResetOtps
            .Where(o => o.UserId == userId && o.VerifiedAt == null && o.ExpiresAt > DateTime.UtcNow)
            .ToListAsync(ct);

        if (oldOnes.Count > 0)
            _context.PasswordResetOtps.RemoveRange(oldOnes);
    }

    public Task<PasswordResetOtp?> GetByResetTokenHashAsync(Guid userId, string resetTokenHash, CancellationToken ct = default) =>
        _context.PasswordResetOtps.FirstOrDefaultAsync(o =>
            o.UserId == userId &&
            o.ResetTokenHash == resetTokenHash &&
            o.ResetTokenExpiresAt != null &&
            o.ResetTokenExpiresAt > DateTime.UtcNow, ct);
}
