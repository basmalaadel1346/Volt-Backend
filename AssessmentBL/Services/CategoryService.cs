using AssessmentBL.DTOs.Taxonomy;
using AssessmentBL.Interfaces;
using AssessmentDA.Context;
using AssessmentDA.Entities;
using Microsoft.EntityFrameworkCore;
using Shared.Common.Exceptions;
using System.Linq.Expressions;

namespace AssessmentBL.Services
{
    /// <summary>
    /// Admin authoring of Assessment categories: the top of the Category → Topic
    /// classification that questions, topic statistics and the child's progress
    /// map use. Not the Content module's levels — a level is where something is
    /// taught, a category is what a question is about.
    ///
    /// Nothing is deleted: a category is deactivated, which takes it off the
    /// progress map without touching a single statistic.
    /// </summary>
    public class CategoryService : ICategoryService
    {
        // Categories.Name and CategoryTranslations.Name are NVARCHAR(100).
        private const int MaxNameLength = 100;

        private static readonly Expression<Func<Category, AdminCategoryResponseDto>> ProjectToResponse =
            category => new AdminCategoryResponseDto
            {
                Id = category.Id,
                Name = category.Name,
                SortOrder = category.SortOrder,
                IsActive = category.IsActive,
                TopicsCount = category.Topics.Count,
                Translations = category.CategoryTranslations
                    .OrderBy(t => t.LanguageCode)
                    .Select(t => new CategoryTranslationDto { LanguageCode = t.LanguageCode, Name = t.Name })
                    .ToList()
            };

        private readonly AssessmentDbContext _db;

        public CategoryService(AssessmentDbContext db) => _db = db;

        public async Task<IReadOnlyList<AdminCategoryResponseDto>> GetAllAsync(
            bool? isActive, CancellationToken cancellationToken = default)
        {
            var query = _db.Categories.AsNoTracking();

            if (isActive.HasValue)
                query = query.Where(c => c.IsActive == isActive.Value);

            return await query
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Id)
                .Select(ProjectToResponse)
                .ToListAsync(cancellationToken);
        }

        public async Task<AdminCategoryResponseDto> GetByIdAsync(byte categoryId, CancellationToken cancellationToken = default)
        {
            var category = await _db.Categories
                .AsNoTracking()
                .Where(c => c.Id == categoryId)
                .Select(ProjectToResponse)
                .FirstOrDefaultAsync(cancellationToken);

            return category ?? throw new KeyNotFoundException($"التصنيف رقم {categoryId} غير موجود");
        }

        public async Task<AdminCategoryResponseDto> CreateAsync(CreateCategoryDto request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var name = TaxonomyInput.RequiredName(request.Name, MaxNameLength, "اسم التصنيف");
            var translations = CheckTranslations(request.Translations ?? []);

            await EnsureNameIsFreeAsync(name, excludingCategoryId: null, cancellationToken);

            // Categories.Id is a TINYINT assigned here, not an IDENTITY: the lowest id
            // not in use, so the ids below seeded rows (which start at 101) are used
            // too and all 255 stay available. Two creates at once can pick the same
            // id; PK_Categories turns the loser into a 409.
            var usedIds = (await _db.Categories.AsNoTracking().Select(c => c.Id).ToListAsync(cancellationToken))
                .ToHashSet();

            var freeId = Enumerable.Range(1, byte.MaxValue).FirstOrDefault(id => !usedIds.Contains((byte)id));

            if (freeId == 0)
                throw new BusinessRuleException($"تم الوصول إلى الحد الأقصى لعدد التصنيفات ({byte.MaxValue})");

            var category = new Category
            {
                Id = (byte)freeId,
                Name = name,
                SortOrder = request.SortOrder,
                // Set explicitly: the DB default is 1, but false is also the CLR
                // default, so EF could not tell "unset" from "inactive".
                IsActive = true
            };

            foreach (var translation in translations)
                category.CategoryTranslations.Add(new CategoryTranslation
                {
                    LanguageCode = translation.LanguageCode,
                    Name = translation.Name
                });

            _db.Categories.Add(category);
            await SaveAsync(name, cancellationToken);

            return await GetByIdAsync(category.Id, cancellationToken);
        }

        public async Task<AdminCategoryResponseDto> UpdateAsync(
            byte categoryId, UpdateCategoryDto request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var category = await _db.Categories
                .Include(c => c.CategoryTranslations)
                .FirstOrDefaultAsync(c => c.Id == categoryId, cancellationToken)
                ?? throw new KeyNotFoundException($"التصنيف رقم {categoryId} غير موجود");

            var name = TaxonomyInput.RequiredName(request.Name, MaxNameLength, "اسم التصنيف");
            var translations = request.Translations is null ? null : CheckTranslations(request.Translations);

            if (!string.Equals(category.Name, name, StringComparison.Ordinal))
                await EnsureNameIsFreeAsync(name, categoryId, cancellationToken);

            category.Name = name;
            category.SortOrder = request.SortOrder;
            category.IsActive = request.IsActive;

            if (translations is not null)
                ReplaceTranslations(category, translations);

            await SaveAsync(name, cancellationToken);

            return await GetByIdAsync(categoryId, cancellationToken);
        }

        public async Task SetActiveAsync(byte categoryId, bool isActive, CancellationToken cancellationToken = default)
        {
            var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == categoryId, cancellationToken)
                ?? throw new KeyNotFoundException($"التصنيف رقم {categoryId} غير موجود");

            if (category.IsActive == isActive)
                return;

            category.IsActive = isActive;
            await _db.SaveChangesAsync(cancellationToken);
        }

        private static List<CheckedTranslation> CheckTranslations(IEnumerable<CategoryTranslationDto?> translations) =>
            TaxonomyInput.Translations(
                translations.Select(t => t is null ? null : new TranslationInput(t.LanguageCode, t.Name, null)),
                MaxNameLength);

        private void ReplaceTranslations(Category category, IReadOnlyList<CheckedTranslation> translations)
        {
            foreach (var existing in category.CategoryTranslations.ToList())
            {
                var incoming = translations.FirstOrDefault(
                    t => string.Equals(t.LanguageCode, existing.LanguageCode, StringComparison.OrdinalIgnoreCase));

                if (incoming is null)
                    _db.CategoryTranslations.Remove(existing);
                else
                    existing.Name = incoming.Name;
            }

            foreach (var translation in translations.Where(t => !category.CategoryTranslations.Any(
                         e => string.Equals(e.LanguageCode, t.LanguageCode, StringComparison.OrdinalIgnoreCase))))
                category.CategoryTranslations.Add(new CategoryTranslation
                {
                    LanguageCode = translation.LanguageCode,
                    Name = translation.Name
                });
        }

        // Mirrors UQ_Categories_Name; SaveAsync covers the race.
        private async Task EnsureNameIsFreeAsync(string name, byte? excludingCategoryId, CancellationToken cancellationToken)
        {
            var taken = await _db.Categories.AsNoTracking().AnyAsync(
                c => c.Name == name && (excludingCategoryId == null || c.Id != excludingCategoryId.Value),
                cancellationToken);

            if (taken)
                throw new ConflictException($"يوجد تصنيف آخر بالاسم '{name}'");
        }

        private async Task SaveAsync(string name, CancellationToken cancellationToken)
        {
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (ex.IsUniqueViolationOf("UQ_Categories_Name"))
            {
                throw new ConflictException($"يوجد تصنيف آخر بالاسم '{name}'", ex);
            }
            catch (DbUpdateException ex) when (ex.IsUniqueViolationOf("PK_Categories"))
            {
                throw new ConflictException("تم إنشاء تصنيف آخر في نفس اللحظة، برجاء إعادة المحاولة", ex);
            }
        }
    }
}
