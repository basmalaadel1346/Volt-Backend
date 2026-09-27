using AssessmentBL.DTOs.Taxonomy;
using AssessmentBL.Interfaces;
using AssessmentBL.Services.Constants;
using AssessmentDA.Context;
using AssessmentDA.Entities;
using Microsoft.EntityFrameworkCore;
using Shared.Common.Abstractions;
using Shared.Common.Exceptions;
using System.Linq.Expressions;

namespace AssessmentBL.Services
{
    /// <summary>
    /// Admin authoring of Assessment topics — what a question is about, and the
    /// unit of the child's topic statistics and progress map. A question does not
    /// need one (Questions.TopicId is optional).
    ///
    /// Nothing is deleted: a topic is deactivated. Its questions keep it, attempt
    /// snapshots keep it, and children who practised it keep seeing their progress.
    /// </summary>
    public class TopicService : ITopicService
    {
        // Topics.Name and TopicTranslations.Name are NVARCHAR(200).
        private const int MaxNameLength = 200;

        private static readonly Expression<Func<Topic, AdminTopicResponseDto>> ProjectToResponse =
            topic => new AdminTopicResponseDto
            {
                Id = topic.Id,
                Name = topic.Name,
                Description = topic.Description,
                CategoryId = topic.CategoryId,
                CategoryName = topic.Category.Name,
                LearningLevel = topic.LearningLevel,
                IsActive = topic.IsActive,
                QuestionsCount = topic.Questions.Count,
                CreatedAt = topic.CreatedAt,
                UpdatedAt = topic.UpdatedAt,
                Translations = topic.TopicTranslations
                    .OrderBy(t => t.LanguageCode)
                    .Select(t => new TopicTranslationDto
                    {
                        LanguageCode = t.LanguageCode,
                        Name = t.Name,
                        Description = t.Description
                    })
                    .ToList()
            };

        private readonly AssessmentDbContext _db;
        private readonly IDateTimeProvider _clock;

        public TopicService(AssessmentDbContext db, IDateTimeProvider clock)
        {
            _db = db;
            _clock = clock;
        }

        public async Task<IReadOnlyList<AdminTopicResponseDto>> GetAsync(
            TopicFilterDto filter, CancellationToken cancellationToken = default)
        {
            var query = _db.Topics.AsNoTracking();

            if (filter?.CategoryId is byte categoryId)
                query = query.Where(t => t.CategoryId == categoryId);

            if (!string.IsNullOrWhiteSpace(filter?.LearningLevel))
            {
                var learningLevel = NormalizeLearningLevel(filter.LearningLevel);
                query = query.Where(t => t.LearningLevel == learningLevel);
            }

            if (filter?.IsActive is bool isActive)
                query = query.Where(t => t.IsActive == isActive);

            // Topics are few (a curriculum, not user data), so no paging.
            return await query
                .OrderBy(t => t.Category.SortOrder)
                .ThenBy(t => t.CategoryId)
                .ThenBy(t => t.Id)
                .Select(ProjectToResponse)
                .ToListAsync(cancellationToken);
        }

        public async Task<AdminTopicResponseDto> GetByIdAsync(int topicId, CancellationToken cancellationToken = default)
        {
            var topic = await _db.Topics
                .AsNoTracking()
                .Where(t => t.Id == topicId)
                .Select(ProjectToResponse)
                .FirstOrDefaultAsync(cancellationToken);

            return topic ?? throw new KeyNotFoundException($"الموضوع رقم {topicId} غير موجود");
        }

        public async Task<AdminTopicResponseDto> CreateAsync(CreateTopicDto request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var name = TaxonomyInput.RequiredName(request.Name, MaxNameLength, "اسم الموضوع");
            var learningLevel = NormalizeLearningLevel(request.LearningLevel);
            var translations = CheckTranslations(request.Translations ?? []);

            await EnsureCategoryExistsAsync(request.CategoryId, cancellationToken);
            await EnsureNameIsFreeAsync(name, excludingTopicId: null, cancellationToken);

            var topic = new Topic
            {
                Name = name,
                Description = TaxonomyInput.OptionalText(request.Description),
                CategoryId = request.CategoryId,
                LearningLevel = learningLevel,
                // Set explicitly: the DB default is 1, but false is also the CLR
                // default, so EF could not tell "unset" from "inactive".
                IsActive = true,
                CreatedAt = _clock.UtcNow
            };

            foreach (var translation in translations)
                topic.TopicTranslations.Add(new TopicTranslation
                {
                    LanguageCode = translation.LanguageCode,
                    Name = translation.Name,
                    Description = translation.Description
                });

            _db.Topics.Add(topic);
            await SaveAsync(name, cancellationToken);

            return await GetByIdAsync(topic.Id, cancellationToken);
        }

        public async Task<AdminTopicResponseDto> UpdateAsync(
            int topicId, UpdateTopicDto request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var topic = await _db.Topics
                .Include(t => t.TopicTranslations)
                .FirstOrDefaultAsync(t => t.Id == topicId, cancellationToken)
                ?? throw new KeyNotFoundException($"الموضوع رقم {topicId} غير موجود");

            var name = TaxonomyInput.RequiredName(request.Name, MaxNameLength, "اسم الموضوع");
            var learningLevel = NormalizeLearningLevel(request.LearningLevel);
            var translations = request.Translations is null ? null : CheckTranslations(request.Translations);

            if (topic.CategoryId != request.CategoryId)
                await EnsureCategoryExistsAsync(request.CategoryId, cancellationToken);

            if (!string.Equals(topic.Name, name, StringComparison.Ordinal))
                await EnsureNameIsFreeAsync(name, topicId, cancellationToken);

            topic.Name = name;
            topic.Description = TaxonomyInput.OptionalText(request.Description);
            topic.CategoryId = request.CategoryId;
            topic.LearningLevel = learningLevel;
            topic.IsActive = request.IsActive;
            topic.UpdatedAt = _clock.UtcNow;

            if (translations is not null)
                ReplaceTranslations(topic, translations);

            await SaveAsync(name, cancellationToken);

            return await GetByIdAsync(topicId, cancellationToken);
        }

        public async Task SetActiveAsync(int topicId, bool isActive, CancellationToken cancellationToken = default)
        {
            var topic = await _db.Topics.FirstOrDefaultAsync(t => t.Id == topicId, cancellationToken)
                ?? throw new KeyNotFoundException($"الموضوع رقم {topicId} غير موجود");

            if (topic.IsActive == isActive)
                return;

            topic.IsActive = isActive;
            topic.UpdatedAt = _clock.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);
        }

        // Mirrors CK_Topics_LearningLevel, accepting any casing.
        private static string NormalizeLearningLevel(string? learningLevel)
        {
            if (string.IsNullOrWhiteSpace(learningLevel))
                throw new ArgumentException(
                    $"مستوى التعلّم مطلوب ({string.Join(" | ", TopicLearningLevels.All)})", nameof(learningLevel));

            return TopicLearningLevels.All.FirstOrDefault(
                       l => string.Equals(l, learningLevel.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException(
                    $"مستوى التعلّم '{learningLevel}' غير صالح ({string.Join(" | ", TopicLearningLevels.All)})",
                    nameof(learningLevel));
        }

        private static List<CheckedTranslation> CheckTranslations(IEnumerable<TopicTranslationDto?> translations) =>
            TaxonomyInput.Translations(
                translations.Select(t => t is null ? null : new TranslationInput(t.LanguageCode, t.Name, t.Description)),
                MaxNameLength);

        private void ReplaceTranslations(Topic topic, IReadOnlyList<CheckedTranslation> translations)
        {
            foreach (var existing in topic.TopicTranslations.ToList())
            {
                var incoming = translations.FirstOrDefault(
                    t => string.Equals(t.LanguageCode, existing.LanguageCode, StringComparison.OrdinalIgnoreCase));

                if (incoming is null)
                {
                    _db.TopicTranslations.Remove(existing);
                }
                else
                {
                    existing.Name = incoming.Name;
                    existing.Description = incoming.Description;
                }
            }

            foreach (var translation in translations.Where(t => !topic.TopicTranslations.Any(
                         e => string.Equals(e.LanguageCode, t.LanguageCode, StringComparison.OrdinalIgnoreCase))))
                topic.TopicTranslations.Add(new TopicTranslation
                {
                    LanguageCode = translation.LanguageCode,
                    Name = translation.Name,
                    Description = translation.Description
                });
        }

        private async Task EnsureCategoryExistsAsync(byte categoryId, CancellationToken cancellationToken)
        {
            if (!await _db.Categories.AsNoTracking().AnyAsync(c => c.Id == categoryId, cancellationToken))
                throw new KeyNotFoundException($"التصنيف رقم {categoryId} غير موجود");
        }

        // Mirrors UQ_Topics_Name; SaveAsync covers the race.
        private async Task EnsureNameIsFreeAsync(string name, int? excludingTopicId, CancellationToken cancellationToken)
        {
            var taken = await _db.Topics.AsNoTracking().AnyAsync(
                t => t.Name == name && (excludingTopicId == null || t.Id != excludingTopicId.Value),
                cancellationToken);

            if (taken)
                throw new ConflictException($"يوجد موضوع آخر بالاسم '{name}'");
        }

        private async Task SaveAsync(string name, CancellationToken cancellationToken)
        {
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (ex.IsUniqueViolationOf("UQ_Topics_Name"))
            {
                throw new ConflictException($"يوجد موضوع آخر بالاسم '{name}'", ex);
            }
        }
    }
}
