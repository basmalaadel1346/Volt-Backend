using AssessmentBL.Services.Constants;
using AssessmentBL.DTOs.Quiz;
using AssessmentBL.DTOs.Quiz.Common;
using AssessmentBL.Interfaces;
using AssessmentDA.Context;
using AssessmentDA.Entities;
using Microsoft.EntityFrameworkCore;
using Shared.Common.Abstractions;
using Shared.Common.Exceptions;
using Shared.Common.Text;
using Shared.Content;
using System.Linq.Expressions;

namespace AssessmentBL.Services
{
    public class QuizService : IQuizService
    {
        private const int MaxPageSize = 100;

        // Kept as an Expression so EF Core translates it into a SQL column
        // list instead of materializing whole Quiz entities client-side.
        private static readonly Expression<Func<Quiz, QuizResponseDto>> ProjectToResponse =
            quiz => new QuizResponseDto
            {
                Id = quiz.Id,
                Title = quiz.Title,
                Description = quiz.Description,
                QuizType = quiz.QuizType,
                LevelId = quiz.LevelId,
                LessonId = quiz.LessonId,
                IsActive = quiz.IsActive,
                CreatedAt = quiz.CreatedAt,
                UpdatedAt = quiz.UpdatedAt
            };

        private readonly AssessmentDbContext _db;
        private readonly IDateTimeProvider _clock;
        private readonly ILessonAvailability _lessons;
        private readonly ILevelCatalog _levels;

        public QuizService(
            AssessmentDbContext db,
            IDateTimeProvider clock,
            ILessonAvailability lessons,
            ILevelCatalog levels)
        {
            _db = db;
            _clock = clock;
            _lessons = lessons;
            _levels = levels;
        }

        public async Task<QuizResponseDto> GetByIdAsync(int quizId, CancellationToken cancellationToken = default)
        {
            var quiz = await _db.Quizzes
                .AsNoTracking()
                .Where(q => q.Id == quizId)
                .Select(ProjectToResponse)
                .FirstOrDefaultAsync(cancellationToken: cancellationToken);

            return quiz ?? throw new KeyNotFoundException($"الاختبار رقم {quizId} غير موجود");
        }

        public async Task<PagedResult<QuizResponseDto>> GetAsync(QuizFilterDto filter, CancellationToken cancellationToken = default)
        {
            var pageNumber = filter.PageNumber < 1 ? 1 : filter.PageNumber;
            var pageSize = filter.PageSize < 1 ? 1
                : filter.PageSize > MaxPageSize ? MaxPageSize
                : filter.PageSize;

            var query = _db.Quizzes.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(filter.QuizType))
            {
                // Canonicalized first: the column holds the exact CHECK-constraint
                // spelling, so filtering on "lessonquiz" must not silently return
                // nothing. An unknown type still matches nothing, as it should.
                var quizType = QuizTypes.Normalize(filter.QuizType) ?? filter.QuizType.Trim();
                query = query.Where(q => q.QuizType == quizType);
            }

            if (filter.LevelId.HasValue)
                query = query.Where(q => q.LevelId == filter.LevelId.Value);

            if (filter.LessonId.HasValue)
                query = query.Where(q => q.LessonId == filter.LessonId.Value);

            if (filter.IsActive.HasValue)
                query = query.Where(q => q.IsActive == filter.IsActive.Value);

            var totalCount = await query.CountAsync(cancellationToken: cancellationToken);

            // Ordering by the clustered PK keeps paging deterministic and
            // lets SQL Server serve the OFFSET/FETCH straight from the index.
            var items = await query
                .OrderByDescending(q => q.Id)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(ProjectToResponse)
                .ToListAsync(cancellationToken: cancellationToken);

            return new PagedResult<QuizResponseDto>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        public async Task<LessonQuizResponseDto> GetForLessonAsync(
            int lessonId,
            string? language = null,
            CancellationToken cancellationToken = default)
        {
            var resolvedLanguage = ContentLanguages.Normalize(language);

            // Lessons live in the Content module and Quizzes.LessonId has no FK
            // (see QuizConfiguration), so existence and visibility are asked of
            // that module rather than assumed. A lesson a child cannot see is
            // reported exactly like one that does not exist.
            var isPublished = await _lessons.IsPublishedAsync(lessonId, cancellationToken);

            if (isPublished != true)
                throw new KeyNotFoundException($"الدرس رقم {lessonId} غير موجود");

            // CK_Quizzes_TypeMatchesReference ties a LessonQuiz to exactly one
            // lesson, but nothing stops a lesson having several; the newest active
            // one with at least one active question wins, deterministically.
            var quiz = await _db.Quizzes
                .AsNoTracking()
                .Where(q => q.LessonId == lessonId
                         && q.QuizType == QuizTypes.LessonQuiz
                         && q.IsActive
                         && q.Questions.Any(question => question.IsActive))
                .OrderByDescending(q => q.Id)
                .Select(q => new QuizHeader
                {
                    Id = q.Id,
                    Title =
                        q.QuizTranslations
                            .Where(t => t.LanguageCode == resolvedLanguage)
                            .Select(t => t.Title)
                            .FirstOrDefault()
                        ?? q.QuizTranslations
                            .Where(t => t.LanguageCode == ContentLanguages.Fallback)
                            .Select(t => t.Title)
                            .FirstOrDefault()
                        ?? q.Title,
                    Description =
                        q.QuizTranslations
                            .Where(t => t.LanguageCode == resolvedLanguage)
                            .Select(t => t.Description)
                            .FirstOrDefault()
                        ?? q.QuizTranslations
                            .Where(t => t.LanguageCode == ContentLanguages.Fallback)
                            .Select(t => t.Description)
                            .FirstOrDefault()
                        ?? q.Description,
                    HasRequestedTranslation = q.QuizTranslations.Any(t => t.LanguageCode == resolvedLanguage),
                    TotalQuestions = q.Questions.Count(question => question.IsActive),
                    TotalPoints = q.Questions
                        .Where(question => question.IsActive)
                        .Sum(question => (int)question.Points)
                })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException($"لا يوجد اختبار متاح للدرس رقم {lessonId}");

            return new LessonQuizResponseDto
            {
                QuizId = quiz.Id,
                LessonId = lessonId,
                Title = quiz.Title,
                Description = quiz.Description,
                TotalQuestions = (short)quiz.TotalQuestions,
                TotalPoints = quiz.TotalPoints,
                Language = resolvedLanguage,
                LanguageFallbackApplied = !quiz.HasRequestedTranslation
            };
        }

        /// <summary>
        /// The quiz to open for a level: its newest ACTIVE LevelAssessment quiz
        /// that actually has active questions. Same shape and same rules as the
        /// per-lesson lookup, so the app resolves "level → quizId" with one call
        /// instead of paging the admin list.
        /// </summary>
        public async Task<LevelQuizResponseDto> GetForLevelAsync(
            int levelId,
            string? language = null,
            CancellationToken cancellationToken = default)
        {
            var resolvedLanguage = ContentLanguages.Normalize(language);

            // Levels live in the Content module and Quizzes.LevelId has no FK, so
            // existence is asked of that module rather than assumed.
            var levels = await _levels.GetLevelsInOrderAsync(cancellationToken);

            if (!levels.Any(l => l.Id == levelId))
                throw new KeyNotFoundException($"المستوى رقم {levelId} غير موجود");

            var quiz = await _db.Quizzes
                .AsNoTracking()
                .Where(q => q.LevelId == levelId
                         && q.QuizType == QuizTypes.LevelAssessment
                         && q.IsActive
                         && q.Questions.Any(question => question.IsActive))
                .OrderByDescending(q => q.Id)
                .Select(q => new QuizHeader
                {
                    Id = q.Id,
                    Title =
                        q.QuizTranslations
                            .Where(t => t.LanguageCode == resolvedLanguage)
                            .Select(t => t.Title)
                            .FirstOrDefault()
                        ?? q.QuizTranslations
                            .Where(t => t.LanguageCode == ContentLanguages.Fallback)
                            .Select(t => t.Title)
                            .FirstOrDefault()
                        ?? q.Title,
                    Description =
                        q.QuizTranslations
                            .Where(t => t.LanguageCode == resolvedLanguage)
                            .Select(t => t.Description)
                            .FirstOrDefault()
                        ?? q.QuizTranslations
                            .Where(t => t.LanguageCode == ContentLanguages.Fallback)
                            .Select(t => t.Description)
                            .FirstOrDefault()
                        ?? q.Description,
                    HasRequestedTranslation = q.QuizTranslations.Any(t => t.LanguageCode == resolvedLanguage),
                    TotalQuestions = q.Questions.Count(question => question.IsActive),
                    TotalPoints = q.Questions
                        .Where(question => question.IsActive)
                        .Sum(question => (int)question.Points)
                })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException($"لا يوجد اختبار متاح للمستوى رقم {levelId}");

            return new LevelQuizResponseDto
            {
                LevelId = levelId,
                QuizId = quiz.Id,
                Title = quiz.Title,
                Description = quiz.Description,
                TotalQuestions = (short)quiz.TotalQuestions,
                TotalPoints = quiz.TotalPoints,
                Language = resolvedLanguage,
                LanguageFallbackApplied = !quiz.HasRequestedTranslation
            };
        }

        /// <summary>A quiz's localized heading and totals — the shape both lookups project into.</summary>
        private sealed class QuizHeader
        {
            public int Id { get; init; }
            public string Title { get; init; } = null!;
            public string? Description { get; init; }
            public bool HasRequestedTranslation { get; init; }
            public int TotalQuestions { get; init; }
            public int TotalPoints { get; init; }
        }

        public async Task<QuizResponseDto> CreateAsync(CreateQuizDto request, CancellationToken cancellationToken = default)
        {
            var title = NormalizeTitle(request.Title);
            var quizType = string.IsNullOrWhiteSpace(request.QuizType)
                ? QuizTypes.Standalone
                : NormalizeQuizType(request.QuizType);

            ValidateTypeMatchesReference(quizType, request.LevelId, request.LessonId);
            await EnsureReferenceExistsAsync(request.LevelId, request.LessonId, cancellationToken);

            var quiz = new Quiz
            {
                Title = title,
                Description = request.Description,
                QuizType = quizType,
                LevelId = request.LevelId,
                LessonId = request.LessonId,
                // Set explicitly: the DB default is 1 for databases built before
                // migration 003, and a draft must be a draft on every database.
                // No slot check here — a draft takes no slot.
                IsActive = false,
                CreatedAt = _clock.UtcNow
            };

            _db.Quizzes.Add(quiz);
            await SaveWithSlotConflictAsync(cancellationToken);

            return ToResponse(quiz);
        }

        public async Task<QuizResponseDto> UpdateAsync(int quizId, UpdateQuizDto request, CancellationToken cancellationToken = default)
        {
            var quiz = await _db.Quizzes.FirstOrDefaultAsync(q => q.Id == quizId, cancellationToken: cancellationToken)
                ?? throw new KeyNotFoundException($"الاختبار رقم {quizId} غير موجود");

            // QuizType / LevelId / LessonId are absent from UpdateQuizDto by
            // design — a quiz cannot be re-pointed at a different level or
            // lesson after creation, which also keeps
            // CK_Quizzes_TypeMatchesReference satisfied.
            //
            // Activating through PUT takes over the slot exactly like
            // PATCH .../active does, so the two never disagree.
            if (request.IsActive && !quiz.IsActive)
                await TakeOverSlotAsync(quiz, cancellationToken);

            quiz.Title = NormalizeTitle(request.Title);
            quiz.Description = request.Description;
            quiz.IsActive = request.IsActive;
            quiz.UpdatedAt = _clock.UtcNow;

            await SaveWithSlotConflictAsync(cancellationToken);

            return ToResponse(quiz);
        }

        /// <summary>
        /// Publishes a draft, or takes a running quiz back to draft. This — not
        /// creation — is where "only one active placement test" and "only one
        /// active quiz per lesson" apply, and the quiz that held the slot is
        /// retired automatically in the same transaction, so the admin never has
        /// to stop the old one by hand first.
        /// </summary>
        public async Task SetActiveAsync(int quizId, bool isActive, CancellationToken cancellationToken = default)
        {
            var quiz = await _db.Quizzes.FirstOrDefaultAsync(q => q.Id == quizId, cancellationToken: cancellationToken)
                ?? throw new KeyNotFoundException($"الاختبار رقم {quizId} غير موجود");

            if (quiz.IsActive == isActive)
                return;

            if (isActive)
                await TakeOverSlotAsync(quiz, cancellationToken);

            quiz.IsActive = isActive;
            quiz.UpdatedAt = _clock.UtcNow;

            await SaveWithSlotConflictAsync(cancellationToken);
        }

        /// <summary>
        /// LevelId / LessonId point into the Content module with no FK (see
        /// QuizConfiguration), so their existence is checked here instead. An
        /// unpublished lesson is allowed — its quiz is authored before release.
        /// </summary>
        private async Task EnsureReferenceExistsAsync(int? levelId, int? lessonId, CancellationToken cancellationToken)
        {
            if (lessonId is int lesson && await _lessons.IsPublishedAsync(lesson, cancellationToken) is null)
                throw new KeyNotFoundException($"الدرس رقم {lesson} غير موجود");

            if (levelId is int level)
            {
                var levels = await _levels.GetLevelsInOrderAsync(cancellationToken);

                if (!levels.Any(l => l.Id == level))
                    throw new KeyNotFoundException($"المستوى رقم {level} غير موجود");
            }
        }

        /// <summary>
        /// Some quiz types allow only one ACTIVE quiz at a time — one Placement
        /// test overall, one LessonQuiz per lesson, one LevelSkip per level —
        /// because otherwise which quiz a child gets would be a guess. Drafts are
        /// unlimited; the rule bites only here, when a draft is published.
        ///
        /// Rather than refusing with "stop the old one first", the quiz currently
        /// holding the slot is moved back to draft in this same SaveChanges: the
        /// swap is atomic, so there is never an instant with two active quizzes
        /// (which the filtered unique indexes would reject) or none.
        /// </summary>
        private async Task TakeOverSlotAsync(Quiz quiz, CancellationToken cancellationToken)
        {
            var incumbents = quiz.QuizType switch
            {
                QuizTypes.Placement => await _db.Quizzes
                    .Where(q => q.QuizType == QuizTypes.Placement && q.IsActive && q.Id != quiz.Id)
                    .ToListAsync(cancellationToken),

                QuizTypes.LessonQuiz => await _db.Quizzes
                    .Where(q => q.QuizType == QuizTypes.LessonQuiz
                             && q.LessonId == quiz.LessonId
                             && q.IsActive
                             && q.Id != quiz.Id)
                    .ToListAsync(cancellationToken),

                QuizTypes.LevelSkip => await _db.Quizzes
                    .Where(q => q.QuizType == QuizTypes.LevelSkip
                             && q.LevelId == quiz.LevelId
                             && q.IsActive
                             && q.Id != quiz.Id)
                    .ToListAsync(cancellationToken),

                _ => []
            };

            foreach (var incumbent in incumbents)
            {
                incumbent.IsActive = false;
                incumbent.UpdatedAt = _clock.UtcNow;
            }
        }

        private async Task SaveWithSlotConflictAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (
                ex.IsUniqueViolationOf("UQ_Quizzes_OneActivePlacement")
             || ex.IsUniqueViolationOf("UQ_Quizzes_OneActiveLessonQuizPerLesson")
             || ex.IsUniqueViolationOf("UQ_Quizzes_OneActiveLevelSkipPerLevel"))
            {
                // TakeOverSlotAsync retires the incumbent, so this is only reached
                // when another admin published into the same slot at the same
                // instant. Nothing was saved; publishing again succeeds.
                throw new ConflictException(
                    "تم تفعيل اختبار آخر لنفس الغرض في نفس اللحظة، برجاء إعادة المحاولة", ex);
            }
        }

        private static string NormalizeTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("عنوان الاختبار مطلوب", nameof(title));

            var trimmed = title.Trim();
            if (trimmed.Length > 300)
                throw new ArgumentException("عنوان الاختبار لا يتجاوز 300 حرف", nameof(title));

            return trimmed;
        }

        // Casing does not matter — "lessonquiz" arrives as the canonical
        // "LessonQuiz", which is what CK_Quizzes_QuizType allows.
        private static string NormalizeQuizType(string quizType) =>
            QuizTypes.Normalize(quizType)
            ?? throw new ArgumentException(
                $"نوع الاختبار '{quizType}' غير صالح ({CanonicalValues.Describe(QuizTypes.All)})", nameof(quizType));

        // Mirrors CK_Quizzes_TypeMatchesReference. Validated here so the caller
        // gets a readable message instead of a raw SQL constraint violation.
        private static void ValidateTypeMatchesReference(string quizType, int? levelId, int? lessonId)
        {
            var valid = quizType switch
            {
                QuizTypes.LevelAssessment or QuizTypes.LevelSkip => levelId.HasValue && !lessonId.HasValue,
                QuizTypes.LessonQuiz or QuizTypes.LessonReview => lessonId.HasValue && !levelId.HasValue,
                QuizTypes.Standalone or QuizTypes.Placement => !levelId.HasValue && !lessonId.HasValue,
                _ => false
            };

            if (!valid)
                throw new ArgumentException(
                    $"نوع الاختبار '{quizType}' لا يتوافق مع LevelId/LessonId المرسلة");
        }

        private static QuizResponseDto ToResponse(Quiz quiz) => new()
        {
            Id = quiz.Id,
            Title = quiz.Title,
            Description = quiz.Description,
            QuizType = quiz.QuizType,
            LevelId = quiz.LevelId,
            LessonId = quiz.LessonId,
            IsActive = quiz.IsActive,
            CreatedAt = quiz.CreatedAt,
            UpdatedAt = quiz.UpdatedAt
        };
    }
}
