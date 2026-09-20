using AssessmentDA.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using AssessmentBL.Interfaces;
using AssessmentBL.Services;

namespace AssessmentBL
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddAssessmentModule(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddDbContext<AssessmentDbContext>(options =>
                options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

            services.Configure<AssessmentSettings>(configuration.GetSection(AssessmentSettings.SectionName));

            services.AddScoped<IQuizAttemptService, QuizAttemptService>();
            services.AddScoped<IUserTopicStatService, UserTopicStatService>();
            services.AddScoped<IQuizService, QuizService>();
            services.AddScoped<IQuestionServiceForAdmin, QuestionService>();
            services.AddScoped<IQuestionOptionServiceForAdmin, QuestionOptionService>();
            services.AddScoped<PlacementEngine>();
            services.AddScoped<AiRequestBuilder>();
            services.AddScoped<IEssayEvaluationService, EssayEvaluationService>();
            services.AddScoped<IHintService, HintService>();
            services.AddScoped<IPlacementService, PlacementService>();
            services.AddScoped<ICategoryService, CategoryService>();
            services.AddScoped<ITopicService, TopicService>();

            return services;
        }
    }
}
