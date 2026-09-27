using Microsoft.EntityFrameworkCore;
using Shared.Common.Abstractions;
using UsersDA.Interfaces;
using UsersDA.Context;
using UsersDA.Entities;

namespace UsersDA.Repositories;

public class EmailVerificationOtpRepository : IEmailVerificationOtpRepository
{
    private readonly UsersDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;

    public EmailVerificationOtpRepository(UsersDbContext context, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task AddAsync(EmailVerificationOtp otp, CancellationToken ct = default) =>
        await _context.EmailVerificationOtps.AddAsync(otp, ct);

    public Task<EmailVerificationOtp?> GetLatestUsableForUserAsync(Guid userId, CancellationToken ct = default) =>
        _context.EmailVerificationOtps
            .Where(o => o.UserId == userId && o.VerifiedAt == null && o.ExpiresAt > _dateTimeProvider.UtcNow)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task DeleteAllUsableForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var oldOnes = await _context.EmailVerificationOtps
            .Where(o => o.UserId == userId && o.VerifiedAt == null && o.ExpiresAt > _dateTimeProvider.UtcNow)
            .ToListAsync(ct);

        if (oldOnes.Count > 0)
            _context.EmailVerificationOtps.RemoveRange(oldOnes);
    }

    // Bulk DELETE في أمر SQL واحد (EF Core 8) - Cleanup دوري للـ OTPs المنتهية من زمان
    public Task<int> DeleteOldExpiredAsync(DateTime olderThan, CancellationToken ct = default) =>
        _context.EmailVerificationOtps
            .Where(o => o.ExpiresAt < olderThan)
            .ExecuteDeleteAsync(ct);
}
