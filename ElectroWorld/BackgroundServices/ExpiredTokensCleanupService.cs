using Shared.Common.Abstractions;
using UsersDA.Interfaces;

namespace ElectroWorld.BackgroundServices;

/// <summary>
/// جدول RefreshTokens وEmailVerificationOtps وPasswordResetOtps مفيهاش أي Cleanup للصفوف
/// المنتهية/الملغية، فهتفضل تكبر من غير أي حد أقصى مع الوقت. الـ Service ده بيشتغل كل 24 ساعة
/// (ومرة أول ما السيرفر يشتغل) ويمسح أي صف بقاله أكتر من 30 يوم منتهي أو ملغي - مش بيمسح حاجة
/// لسه صالحة أو حديثة، فمفيش أي تأثير على أي مستخدم شغال.
/// </summary>
public class ExpiredTokensCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private const int RetentionDays = 30;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExpiredTokensCleanupService> _logger;

    public ExpiredTokensCleanupService(IServiceScopeFactory scopeFactory, ILogger<ExpiredTokensCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunCleanupAsync(stoppingToken);

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // السيرفر بيقفل - طبيعي، مش خطأ
            }
        }
    }

    private async Task RunCleanupAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var refreshTokenRepository = scope.ServiceProvider.GetRequiredService<IRefreshTokenRepository>();
            var emailOtpRepository = scope.ServiceProvider.GetRequiredService<IEmailVerificationOtpRepository>();
            var passwordOtpRepository = scope.ServiceProvider.GetRequiredService<IPasswordResetOtpRepository>();
            var dateTimeProvider = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

            var olderThan = dateTimeProvider.UtcNow.AddDays(-RetentionDays);

            // كل Repository بينفذ ExecuteDeleteAsync بتاعه (بيتنفذ فورًا على الداتابيز، مش محتاج SaveChanges)
            var deletedTokens = await refreshTokenRepository.DeleteOldExpiredOrRevokedAsync(olderThan, ct);
            var deletedEmailOtps = await emailOtpRepository.DeleteOldExpiredAsync(olderThan, ct);
            var deletedPasswordOtps = await passwordOtpRepository.DeleteOldExpiredAsync(olderThan, ct);

            if (deletedTokens + deletedEmailOtps + deletedPasswordOtps > 0)
            {
                _logger.LogInformation(
                    "Cleanup: removed {Tokens} refresh tokens, {EmailOtps} email OTPs, {PasswordOtps} password OTPs older than {Days} days",
                    deletedTokens, deletedEmailOtps, deletedPasswordOtps, RetentionDays);
            }
        }
        catch (Exception ex)
        {
            // فشل الـ Cleanup مايوقفش السيرفر ولا يأثر على أي مستخدم - بس بنسجله عشان نراجعه
            _logger.LogError(ex, "Expired tokens cleanup failed");
        }
    }
}
