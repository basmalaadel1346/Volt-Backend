using Microsoft.EntityFrameworkCore;
using Shared.Common.Abstractions;
using UsersDA.Interfaces;
using UsersDA.Context;
using UsersDA.Entities;

namespace UsersDA.Repositories;

public class PasswordResetOtpRepository : IPasswordResetOtpRepository
{
    private readonly UsersDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;

    public PasswordResetOtpRepository(UsersDbContext context, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task AddAsync(PasswordResetOtp otp, CancellationToken ct = default) =>
        await _context.PasswordResetOtps.AddAsync(otp, ct);

    // أحدث OTP لسه صالح (مش متحقق منه، ومش منتهي) لليوزر ده
    public Task<PasswordResetOtp?> GetLatestUsableForUserAsync(Guid userId, CancellationToken ct = default) =>
        _context.PasswordResetOtps
            .Where(o => o.UserId == userId && o.VerifiedAt == null && o.ExpiresAt > _dateTimeProvider.UtcNow)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task DeleteAllUsableForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var oldOnes = await _context.PasswordResetOtps
            .Where(o => o.UserId == userId && o.VerifiedAt == null && o.ExpiresAt > _dateTimeProvider.UtcNow)
            .ToListAsync(ct);

        if (oldOnes.Count > 0)
            _context.PasswordResetOtps.RemoveRange(oldOnes);
    }

    public Task<PasswordResetOtp?> GetByResetTokenHashAsync(Guid userId, string resetTokenHash, CancellationToken ct = default) =>
        _context.PasswordResetOtps.FirstOrDefaultAsync(o =>
            o.UserId == userId &&
            o.ResetTokenHash == resetTokenHash &&
            o.ResetTokenExpiresAt != null &&
            o.ResetTokenExpiresAt > _dateTimeProvider.UtcNow, ct);

    // Bulk DELETE في أمر SQL واحد (EF Core 8) - Cleanup دوري للـ OTPs المنتهية من زمان.
    // بنفحص ExpiresAt وResetTokenExpiresAt مع بعض عشان لو صف اتحقق منه (Step 1) وبعدين محدش
    // كمّل Reset Password فعليًا، ResetTokenExpiresAt ممكن يكون أبعد من ExpiresAt الأصلي.
    public Task<int> DeleteOldExpiredAsync(DateTime olderThan, CancellationToken ct = default) =>
        _context.PasswordResetOtps
            .Where(o => o.ExpiresAt < olderThan && (o.ResetTokenExpiresAt == null || o.ResetTokenExpiresAt < olderThan))
            .ExecuteDeleteAsync(ct);
}
