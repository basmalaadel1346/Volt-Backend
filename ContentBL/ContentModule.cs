using ContentBL.Interfaces;
using ContentBL.Services;
using ContentDA;
using ContentDA.Context;
using ContentDA.Interfaces;
using ContentDA.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ContentBL;

public static class ContentModule
{
    public static IServiceCollection AddContentModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ContentDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

        services.AddScoped<IContentTypeRepository, ContentTypeRepository>();
        services.AddScoped<ILevelRepository, LevelRepository>();
        services.AddScoped<ILessonRepository, LessonRepository>();
        services.AddScoped<ILessonContentRepository, LessonContentRepository>();
        services.AddScoped<ILearningProgressRepository, LearningProgressRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<IContentTypeService, ContentTypeService>();
        services.AddScoped<ILevelService, LevelService>();
        services.AddScoped<ILessonService, LessonService>();
        services.AddScoped<ILessonContentService, LessonContentService>();
        services.AddScoped<ILearningProgressService, LearningProgressService>();
        services.AddScoped<IImageStorageService, LocalImageStorageService>();
        services.AddScoped<LessonAvailabilityService>();
        services.AddScoped<Shared.Content.ILessonAvailability>(sp => sp.GetRequiredService<LessonAvailabilityService>());
        services.AddScoped<Shared.Content.ILessonProgressWriter>(sp => sp.GetRequiredService<LessonAvailabilityService>());
        services.AddScoped<Shared.Content.ILevelCatalog, LevelCatalogService>();
        services.AddScoped<Shared.Content.IMediaContentReader, LocalMediaContentReader>();
        return services;
    }
}
