using AssessmentBL.Services.Constants;
using AssessmentBL.DTOs.Quiz;
using AssessmentBL.DTOs.Quiz.Common;
using AssessmentBL.Interfaces;
using AssessmentDA.Context;
using AssessmentDA.Entities;
using Microsoft.EntityFrameworkCore;
using Shared.Common.Abstractions;
using Shared.Common.Exceptions;
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
                query = query.Where(q => q.QuizType == filter.QuizType);

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
                .Select(q => new
                {
                    q.Id,
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
                    HasRequestedTranslation = q.QuizTranslations.Any(t => t.LanguageCode == resolvedLanguage)
                })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException($"لا يوجد اختبار متاح للدرس رقم {lessonId}");

            // Same child-safe projection the attempt endpoints use: no IsCorrect,
            // no image descriptions, questions and options in DisplayOrder.
            var questions = await LocalizedQuestionQuery.Project(
                    _db.Questions.AsNoTracking().Where(q => q.QuizId == quiz.Id && q.IsActive),
                    resolvedLanguage)
                .ToListAsync(cancellationToken);

            return new LessonQuizResponseDto
            {
                QuizId = quiz.Id,
                LessonId = lessonId,
                Title = quiz.Title,
                Description = quiz.Description,
                TotalQuestions = (short)questions.Count,
                Language = resolvedLanguage,
                LanguageFallbackApplied = !quiz.HasRequestedTranslation || questions.Any(r => r.UsedFallback),
                Questions = questions.Select(r => r.ToDto()).ToList()
            };
        }

        public async Task<QuizResponseDto> CreateAsync(CreateQuizDto request, CancellationToken cancellationToken = default)
        {
            var title = NormalizeTitle(request.Title);
            var quizType = string.IsNullOrWhiteSpace(request.QuizType)
                ? QuizTypes.Standalone
                : request.QuizType;

            ValidateQuizType(quizType);
            ValidateTypeMatchesReference(quizType, request.LevelId, request.LessonId);
            await EnsureReferenceExistsAsync(request.LevelId, request.LessonId, cancellationToken);

            // A new quiz starts active, so the one-active-per-slot rule applies now.
            await EnsureSlotIsFreeAsync(quizType, request.LessonId, excludingQuizId: null, cancellationToken);

            var quiz = new Quiz
            {
                Title = title,
                Description = request.Description,
                QuizType = quizType,
                LevelId = request.LevelId,
                LessonId = request.LessonId,
                // Set explicitly: the DB default is 1, but `false` is also the
                // CLR default for bool, so EF cannot tell "unset" from
                // "deliberately inactive" on insert.
                IsActive = true,
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
            if (request.IsActive && !quiz.IsActive)
                await EnsureSlotIsFreeAsync(quiz.QuizType, quiz.LessonId, quizId, cancellationToken);

            quiz.Title = NormalizeTitle(request.Title);
            quiz.Description = request.Description;
            quiz.IsActive = request.IsActive;
            quiz.UpdatedAt = _clock.UtcNow;

            await SaveWithSlotConflictAsync(cancellationToken);

            return ToResponse(quiz);
        }

        public async Task SetActiveAsync(int quizId, bool isActive, CancellationToken cancellationToken = default)
        {
            var quiz = await _db.Quizzes.FirstOrDefaultAsync(q => q.Id == quizId, cancellationToken: cancellationToken)
                ?? throw new KeyNotFoundException($"الاختبار رقم {quizId} غير موجود");

            if (quiz.IsActive == isActive)
                return;

            if (isActive)
                await EnsureSlotIsFreeAsync(quiz.QuizType, quiz.LessonId, quizId, cancellationToken);

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
        /// One active Placement quiz overall, and one active LessonQuiz per lesson —
        /// otherwise which quiz a child gets would be a guess. Mirrors the filtered
        /// unique indexes of migration 007; this turns the common case into a
        /// readable message, SaveWithSlotConflictAsync covers the race.
        /// </summary>
        private async Task EnsureSlotIsFreeAsync(
            string quizType,
            int? lessonId,
            int? excludingQuizId,
            CancellationToken cancellationToken)
        {
            var taken = quizType switch
            {
                QuizTypes.Placement => await _db.Quizzes.AsNoTracking().AnyAsync(
                    q => q.QuizType == QuizTypes.Placement
                      && q.IsActive
                      && (excludingQuizId == null || q.Id != excludingQuizId.Value),
                    cancellationToken),

                QuizTypes.LessonQuiz => await _db.Quizzes.AsNoTracking().AnyAsync(
                    q => q.QuizType == QuizTypes.LessonQuiz
                      && q.LessonId == lessonId
                      && q.IsActive
                      && (excludingQuizId == null || q.Id != excludingQuizId.Value),
                    cancellationToken),

                _ => false
            };

            if (taken)
                throw new ConflictException(quizType == QuizTypes.Placement
                    ? "يوجد اختبار تحديد مستوى مفعّل بالفعل، أوقفه أولًا"
                    : $"يوجد اختبار مفعّل بالفعل للدرس رقم {lessonId}، أوقفه أولًا");
        }

        private async Task SaveWithSlotConflictAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (
                ex.IsUniqueViolationOf("UQ_Quizzes_OneActivePlacement")
             || ex.IsUniqueViolationOf("UQ_Quizzes_OneActiveLessonQuizPerLesson"))
            {
                throw new ConflictException("يوجد اختبار مفعّل آخر لنفس الغرض، أوقفه أولًا", ex);
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

        private static void ValidateQuizType(string quizType)
        {
            if (!QuizTypes.All.Contains(quizType))
                throw new ArgumentException($"نوع الاختبار '{quizType}' غير صالح", nameof(quizType));
        }

        // Mirrors CK_Quizzes_TypeMatchesReference. Validated here so the caller
        // gets a readable message instead of a raw SQL constraint violation.
        private static void ValidateTypeMatchesReference(string quizType, int? levelId, int? lessonId)
        {
            var valid = quizType switch
            {
                QuizTypes.LevelAssessment => levelId.HasValue && !lessonId.HasValue,
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
