using UsersDA.Entities;

namespace UsersDA.Interfaces;

public interface IEmailVerificationOtpRepository
{
    Task AddAsync(EmailVerificationOtp otp, CancellationToken ct = default);
    Task<EmailVerificationOtp?> GetLatestUsableForUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>بيمسح أي OTP قديم لسه شغال لليوزر ده (لما يطلب كود جديد، يفضل الكود الأخير بس هو الشغال).</summary>
    Task DeleteAllUsableForUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>بيمسح أي OTP منتهي من زمان (قبل olderThan) - Cleanup دوري عشان الجدول مايكبرش.
    /// بترجع عدد الصفوف اللي اتمسحت.</summary>
    Task<int> DeleteOldExpiredAsync(DateTime olderThan, CancellationToken ct = default);
}
