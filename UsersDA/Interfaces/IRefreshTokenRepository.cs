using UsersDA.Entities;

namespace UsersDA.Interfaces;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default);
    Task AddAsync(RefreshToken token, CancellationToken ct = default);

    /// <summary>بتلغي التوكن ده لو ولو بس لسه شغال (Atomic UPDATE على مستوى الداتابيز نفسها) -
    /// بترجع true لو هي اللي لغته فعليًا، وfalse لو كان اتلغى بالفعل من طلب متزامن تاني
    /// (بتحل مشكلة Race Condition لو جالك طلبين Refresh في نفس اللحظة بنفس التوكن).</summary>
    Task<bool> RevokeIfActiveAsync(Guid tokenId, DateTime now, CancellationToken ct = default);

    /// <summary>بتلغي كل الـ Refresh Tokens الشغالة لليوزر ده دفعة واحدة (مستخدمة بعد تحويل
    /// حساب Guest أو بعد تغيير الباسورد، عشان أي جلسة قديمة تتقفل).</summary>
    Task RevokeAllActiveForUserAsync(Guid userId, DateTime now, CancellationToken ct = default);

    /// <summary>بتمسح أي صف قديم بقاله وقت طويل منتهي أو ملغي (مش لسه صالح) - Cleanup دوري
    /// عشان الجدول مايكبرش من غير حد أقصى. بترجع عدد الصفوف اللي اتمسحت.</summary>
    Task<int> DeleteOldExpiredOrRevokedAsync(DateTime olderThan, CancellationToken ct = default);
}
