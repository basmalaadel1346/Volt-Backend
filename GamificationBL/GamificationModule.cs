using GamificationBL.Interfaces;
using GamificationBL.Services;
using GamificationDA.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shared.Gamification;

namespace GamificationBL;

public static class GamificationModule
{
    public static IServiceCollection AddGamificationModule(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<GamificationDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

        services.Configure<GamificationSettings>(configuration.GetSection(GamificationSettings.SectionName));

        services.AddScoped<IGamificationService, GamificationService>();

        // Replaces the NullLearningRewards that SharedModule registers as a
        // fallback, so Assessment and Content start granting real rewards the
        // moment this module is added — and keep working when it is not.
        services.RemoveAll<ILearningRewards>();
        services.AddScoped<ILearningRewards, LearningRewardsService>();

        return services;
    }
}
