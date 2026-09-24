using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shared.Common.Abstractions;
using Shared.Common.Realtime;
using Shared.Gamification;
using Shared.Common.Email;
using Shared.Common.Implementations;
using Shared.Users;

namespace Shared;

public static class SharedModule
{
    public static IServiceCollection AddShared(this IServiceCollection services, IConfiguration configuration)
    {
        // ---- Common: مالوش أي علاقة بمفهوم "اليوزر"، أي مودل بعدين يقدر يستخدمه ----
        services.Configure<EmailSettings>(configuration.GetSection(EmailSettings.SectionName));
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IHashGenerator, Sha256HashGenerator>();
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddSingleton<IEmailSender, SmtpEmailSender>();

        // ---- Defaults for the cross-module contracts, so every module runs even
        // when the host does not provide a real implementation. TryAdd, so a host
        // that DOES register one (SignalR for the notifier, GamificationBL for the
        // rewards) always wins, whatever the registration order.
        services.TryAddSingleton<ILearnerNotifier>(NullLearnerNotifier.Instance);
        services.TryAddSingleton<ILearningRewards>(NullLearningRewards.Instance);

        // ---- Users: مرتبط بمفهوم هوية المستخدم (JWT / Google) - بيتحط هنا (مش جوه UsersBL)
        // عشان أي مودل تاني يحتاج يتحقق من هوية اليوزر أو يصدر توكن يقدر يستخدمه من غير
        // ما يعمل Reference لمودل الـ Users كله
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.Configure<GoogleSettings>(configuration.GetSection(GoogleSettings.SectionName));
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddSingleton<IGoogleAuthValidator, GoogleAuthValidator>();

        return services;
    }
}
