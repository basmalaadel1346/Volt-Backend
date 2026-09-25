using UsersDA.Entities;

namespace UsersDA.Interfaces;

public interface IPasswordResetOtpRepository
{
    Task AddAsync(PasswordResetOtp otp, CancellationToken ct = default);
    Task<PasswordResetOtp?> GetLatestUsableForUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>بيمسح أي OTP قديم لسه شغال لليوزر ده (لما يطلب كود جديد، يفضل الكود الأخير بس هو الشغال).</summary>
    Task DeleteAllUsableForUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>بيدور على صف بالـ ResetTokenHash المطابق، لليوزر ده، وبيكون لسه صالح ومتحقق منه من قبل (Step 2).</summary>
    Task<PasswordResetOtp?> GetByResetTokenHashAsync(Guid userId, string resetTokenHash, CancellationToken ct = default);
}
