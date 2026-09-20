using AssessmentBL.DTOs.Question;
using AssessmentBL.DTOs.QuestionOption;
using AssessmentBL.Interfaces;
using AssessmentDA.Context;
using AssessmentDA.Entities;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using AssessmentBL.Services.Constants;
using Shared.Common.Exceptions;

namespace AssessmentBL.Services
{
    // for admin use only; children do not create or update questions, they only pick them
    public class QuestionService : IQuestionServiceForAdmin
    {
        // Projects the question together with its options in a single SQL
        // round-trip, so listing a quiz never degenerates into an N+1.
        private static readonly Expression<Func<Question, AdminQuestionResponseDto>> ProjectToResponse =
            question => new AdminQuestionResponseDto
            {
                Id = question.Id,
                QuizId = question.QuizId,
                TopicId = question.TopicId,
                QuestionText = question.QuestionText,
                QuestionType = question.QuestionType,
                ImageUrl = question.ImageUrl,
                ImageDescription = question.ImageDescription,
                Difficulty = question.Difficulty,
                DisplayOrder = question.DisplayOrder,
                Points = question.Points,
                IsActive = question.IsActive,
                CreatedAt = question.CreatedAt,
                Options = question.QuestionOptions
                    .OrderBy(o => o.DisplayOrder)
                    .Select(o => new AdminQuestionOptionResponseDto
                    {
                        Id = o.Id,
                        OptionText = o.OptionText,
                        ImageUrl = o.ImageUrl,
                        ImageDescription = o.ImageDescription,
                        IsCorrect = o.IsCorrect,
                        DisplayOrder = o.DisplayOrder
                    })
                    .ToList()
            };

        private readonly AssessmentDbContext _db;

        public QuestionService(AssessmentDbContext db) => _db = db;

        public async Task<IReadOnlyList<AdminQuestionResponseDto>> GetByQuizIdAsync(int quizId, CancellationToken cancellationToken = default)
        {
            var quizExists = await _db.Quizzes.AsNoTracking().AnyAsync(q => q.Id == quizId, cancellationToken: cancellationToken);
            if (!quizExists)
                throw new KeyNotFoundException($"الاختبار رقم {quizId} غير موجود");

            // Admin listing: inactive questions are included on purpose.
            return await _db.Questions
                .AsNoTracking()
                .Where(q => q.QuizId == quizId)
                .OrderBy(q => q.DisplayOrder)
                .Select(ProjectToResponse)
                .ToListAsync(cancellationToken: cancellationToken);
        }

        public async Task<AdminQuestionResponseDto> GetByQuestionIdAsync(int questionId, CancellationToken cancellationToken = default)
        {
            var question = await _db.Questions
                .AsNoTracking()
                .Where(q => q.Id == questionId)
                .Select(ProjectToResponse)
                .FirstOrDefaultAsync(cancellationToken: cancellationToken);

            return question ?? throw new KeyNotFoundException($"السؤال رقم {questionId} غير موجود");
        }

        public async Task<AdminQuestionResponseDto> CreateQuestionAsync(CreateQuestionDto request, CancellationToken cancellationToken = default)
        {
            var quizType = await _db.Quizzes
                .AsNoTracking()
                .Where(q => q.Id == request.QuizId)
                .Select(q => q.QuizType)
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException($"الاختبار رقم {request.QuizId} غير موجود");

            // The placement quiz owns no questions: it samples each level's
            // LevelAssessment quiz (PlacementEngine). A question added here would
            // never be asked.
            if (quizType == QuizTypes.Placement)
                throw new BusinessRuleException(
                    "اختبار تحديد المستوى يأخذ أسئلته من اختبارات تقييم المستويات، أضف السؤال إلى اختبار تقييم المستوى المناسب");

            if (request.TopicId is int topicId)
                await EnsureTopicExistsAsync(topicId, cancellationToken);

            var questionText = NormalizeQuestionText(request.QuestionText);
            var difficulty = NormalizeDifficulty(request.Difficulty);
            var questionType = NormalizeQuestionType(request.QuestionType);
            var points = NormalizePoints(request.Points);
            var (imageUrl, imageDescription) = NormalizeImage(request.ImageUrl, request.ImageDescription);

            await EnsureDisplayOrderIsFreeAsync(request.QuizId, request.DisplayOrder, excludingQuestionId: null, cancellationToken: cancellationToken);

            var question = new Question
            {
                QuizId = request.QuizId,
                TopicId = request.TopicId,
                QuestionText = questionText,
                QuestionType = questionType,
                ImageUrl = imageUrl,
                ImageDescription = imageDescription,
                Difficulty = difficulty,
                DisplayOrder = request.DisplayOrder,
                Points = points
                // IsActive is left alone on purpose: the DB default is 0, so a
                // new question starts inactive until an admin publishes it via
                // SetActiveAsync (and it stays out of every quiz attempt until
                // then). CreatedAt is filled by the sysutcdatetime() default.
            };

            _db.Questions.Add(question);
            await SaveWithDisplayOrderConflictAsync(request.QuizId, request.DisplayOrder, cancellationToken);

            return await GetByQuestionIdAsync(question.Id, cancellationToken: cancellationToken);
        }

        public async Task<AdminQuestionResponseDto> UpdateQuestionAsync(int questionId, UpdateQuestionDto request, CancellationToken cancellationToken = default)
        {
            var question = await _db.Questions.FirstOrDefaultAsync(q => q.Id == questionId, cancellationToken: cancellationToken)
                ?? throw new KeyNotFoundException($"السؤال رقم {questionId} غير موجود");

            // Null clears the topic, like every other field PUT replaces.
            if (request.TopicId is int newTopicId && question.TopicId != newTopicId)
                await EnsureTopicExistsAsync(newTopicId, cancellationToken);

            var questionText = NormalizeQuestionText(request.QuestionText);
            var difficulty = NormalizeDifficulty(request.Difficulty);
            var questionType = string.IsNullOrWhiteSpace(request.QuestionType)
                ? question.QuestionType
                : NormalizeQuestionType(request.QuestionType);
            var points = NormalizePoints(request.Points);
            var (imageUrl, imageDescription) = NormalizeImage(request.ImageUrl, request.ImageDescription);

            if (question.DisplayOrder != request.DisplayOrder)
                await EnsureDisplayOrderIsFreeAsync(question.QuizId, request.DisplayOrder, excludingQuestionId: questionId, cancellationToken: cancellationToken);

            // Changing type is only safe while the option set still matches the
            // new type's rules, so validate against the type and image being saved.
            if (request.IsActive)
                await EnsureAnswerableAsync(questionId, questionType, imageUrl, imageDescription, cancellationToken);
            // QuizId is not part of UpdateQuestionDto — a question cannot be
            // moved to another quiz, which keeps existing attempt history sane.
            question.TopicId = request.TopicId;
            question.QuestionText = questionText;
            question.QuestionType = questionType;
            question.ImageUrl = imageUrl;
            question.ImageDescription = imageDescription;
            question.Difficulty = difficulty;
            question.DisplayOrder = request.DisplayOrder;
            question.Points = points;
            question.IsActive = request.IsActive;

            await SaveWithDisplayOrderConflictAsync(question.QuizId, request.DisplayOrder, cancellationToken);

            return await GetByQuestionIdAsync(questionId, cancellationToken: cancellationToken);
        }

        public async Task SetActiveAsync(int questionId, bool isActive, CancellationToken cancellationToken = default)
        {
            var question = await _db.Questions.FirstOrDefaultAsync(q => q.Id == questionId, cancellationToken: cancellationToken)
                ?? throw new KeyNotFoundException($"السؤال رقم {questionId} غير موجود");

            if (question.IsActive == isActive)
                return;

            // The stored image is checked, not a request's: this is the path that
            // catches legacy rows saved before an image required a description.
            if (isActive)
                await EnsureAnswerableAsync(
                    questionId, question.QuestionType, question.ImageUrl, question.ImageDescription, cancellationToken);

            question.IsActive = isActive;

            await _db.SaveChangesAsync(cancellationToken: cancellationToken);
        }
        // EnsureDisplayOrderIsFreeAsync is only a friendly pre-check; two admins
        // can pass it concurrently. UQ_Questions_QuizId_DisplayOrder is the real
        // protection, and this turns losing that race into a 409 rather than a 500.
        private async Task SaveWithDisplayOrderConflictAsync(
            int quizId, short displayOrder, CancellationToken cancellationToken)
        {
            try
            {
                await _db.SaveChangesAsync(cancellationToken: cancellationToken);
            }
            catch (DbUpdateException ex) when (ex.IsUniqueViolationOf("UQ_Questions_QuizId_DisplayOrder"))
            {
                throw new ConflictException(
                    $"الترتيب {displayOrder} مستخدم بالفعل في الاختبار رقم {quizId}", ex);
            }
        }

        // A topic is optional; when one is given it must exist, or FK_Questions_Topics
        // would turn the save into a 500.
        private async Task EnsureTopicExistsAsync(int topicId, CancellationToken cancellationToken)
        {
            if (!await _db.Topics.AsNoTracking().AnyAsync(t => t.Id == topicId, cancellationToken))
                throw new KeyNotFoundException($"الموضوع رقم {topicId} غير موجود");
        }

        // Mirrors UQ_Questions_QuizId_DisplayOrder so the admin gets a clear
        // conflict instead of a raw unique-index violation.
        private async Task EnsureDisplayOrderIsFreeAsync(int quizId, short displayOrder, int? excludingQuestionId, CancellationToken cancellationToken = default)
        {
            var taken = await _db.Questions
                .AsNoTracking()
                .AnyAsync(q => q.QuizId == quizId
                            && q.DisplayOrder == displayOrder
                            && (excludingQuestionId == null || q.Id != excludingQuestionId.Value), cancellationToken: cancellationToken);

            if (taken)
                throw new BusinessRuleException(
                    $"الترتيب {displayOrder} مستخدم بالفعل في الاختبار رقم {quizId}");
        }

        private static string NormalizeQuestionText(string? questionText)
        {
            if (string.IsNullOrWhiteSpace(questionText))
                throw new ArgumentException("نص السؤال مطلوب", nameof(questionText));

            return questionText.Trim();
        }

        // Mirrors CK_Questions_Difficulty; an omitted value falls back to the
        // column default rather than failing.
        private static string NormalizeDifficulty(string? difficulty)
        {
            if (string.IsNullOrWhiteSpace(difficulty))
                return QuestionDifficulties.Medium;

            if (!QuestionDifficulties.All.Contains(difficulty))
                throw new ArgumentException($"مستوى الصعوبة '{difficulty}' غير صالح", nameof(difficulty));

            return difficulty;
        }
        // Mirrors CK_Questions_QuestionType.
        private static string NormalizeQuestionType(string? questionType)
        {
            if (string.IsNullOrWhiteSpace(questionType))
                return QuestionTypes.MultipleChoice;

            if (!QuestionTypes.All.Contains(questionType))
                throw new ArgumentException($"نوع السؤال '{questionType}' غير صالح", nameof(questionType));

            return questionType;
        }

        private static string? NormalizeImageUrl(string? imageUrl) =>
            string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl.Trim();

        // Mirrors the NVARCHAR(1000) ImageDescription column.
        private const int MaxImageDescriptionLength = 1000;

        /// <summary>
        /// Mirrors CK_Questions_ImageHasDescription. The AI never looks at images,
        /// only at text, so an image is only usable with a description of what it
        /// shows. Without an image the description is dropped: a description must
        /// never outlive a removed image and describe something that is not there.
        /// </summary>
        private static (string? ImageUrl, string? ImageDescription) NormalizeImage(
            string? imageUrl, string? imageDescription)
        {
            var image = NormalizeImageUrl(imageUrl);
            if (image is null)
                return (null, null);

            if (string.IsNullOrWhiteSpace(imageDescription))
                throw new ArgumentException(
                    "السؤال الذي يحتوي على صورة يجب أن يحتوي على وصف للصورة حتى يتمكن النظام من فهم السؤال",
                    nameof(imageDescription));

            var description = imageDescription.Trim();
            if (description.Length > MaxImageDescriptionLength)
                throw new ArgumentException(
                    $"وصف الصورة لا يتجاوز {MaxImageDescriptionLength} حرف", nameof(imageDescription));

            return (image, description);
        }

        /// <summary>
        /// A question may only be activated when its options match its type:
        /// MultipleChoice needs at least two options and exactly one correct,
        /// TrueFalse needs exactly two options and exactly one correct, and an
        /// Essay must have none at all. Every image — the question's own and each
        /// option's — must also carry a description, because a published question
        /// is what the AI is asked about.
        /// </summary>
        private async Task EnsureAnswerableAsync(
            int questionId,
            string questionType,
            string? imageUrl,
            string? imageDescription,
            CancellationToken cancellationToken = default)
        {
            EnsureQuestionImageIsDescribed(questionId, imageUrl, imageDescription);

            var counts = await _db.QuestionOptions
                .AsNoTracking()
                .Where(o => o.QuestionId == questionId)
                .GroupBy(o => 1)
                .Select(g => new { Total = g.Count(), Correct = g.Count(o => o.IsCorrect) })
                .FirstOrDefaultAsync(cancellationToken);

            var total = counts?.Total ?? 0;
            var correct = counts?.Correct ?? 0;

            if (questionType == QuestionTypes.Essay)
            {
                if (total > 0)
                    throw new BusinessRuleException(
                        $"السؤال المقالي رقم {questionId} لا يجب أن يحتوي على اختيارات");

                return;
            }

            if (questionType == QuestionTypes.TrueFalse && total != 2)
                throw new BusinessRuleException(
                    $"سؤال الصح والخطأ رقم {questionId} يجب أن يحتوي على اختيارين بالضبط");

            if (questionType == QuestionTypes.MultipleChoice && total < 2)
                throw new BusinessRuleException(
                    $"السؤال رقم {questionId} يجب أن يحتوي على اختيارين على الأقل قبل تفعيله");

            if (correct != 1)
                throw new BusinessRuleException(
                    $"السؤال رقم {questionId} يجب أن يحتوي على إجابة صحيحة واحدة بالضبط قبل تفعيله");

            // Same test as CK_QuestionOptions_ImageHasDescription (ImageUrl IS NOT
            // NULL, trimmed description blank). Options saved through
            // QuestionOptionService always pass; this catches rows written before
            // the rule existed, which the migration left in place WITH NOCHECK.
            var undescribedOptionIds = await _db.QuestionOptions
                .AsNoTracking()
                .Where(o => o.QuestionId == questionId
                         && o.ImageUrl != null
                         && (o.ImageDescription == null || o.ImageDescription.Trim() == ""))
                .OrderBy(o => o.DisplayOrder)
                .Select(o => o.Id)
                .ToListAsync(cancellationToken);

            EnsureOptionImagesAreDescribed(questionId, undescribedOptionIds);
        }

        // Uses the database's notion of "has an image" (ImageUrl not null), not the
        // request normalizer's, so a legacy blank ImageUrl is refused here with a
        // clear message instead of failing CK_Questions_ImageHasDescription later.
        private static void EnsureQuestionImageIsDescribed(
            int questionId, string? imageUrl, string? imageDescription)
        {
            if (imageUrl is not null && string.IsNullOrWhiteSpace(imageDescription))
                throw new BusinessRuleException(
                    $"لا يمكن تفعيل السؤال رقم {questionId}: صورة السؤال بدون وصف، أضف وصفًا للصورة أولًا");
        }

        private static void EnsureOptionImagesAreDescribed(
            int questionId, IReadOnlyCollection<int> undescribedOptionIds)
        {
            if (undescribedOptionIds.Count > 0)
                throw new BusinessRuleException(
                    $"لا يمكن تفعيل السؤال رقم {questionId}: صور الاختيارات رقم {string.Join("، ", undescribedOptionIds)} بدون وصف، أضف وصفًا لكل صورة أولًا");
        }

        // Mirrors CK_Questions_Points (> 0); 0 means "not supplied", so the
        // column default of 1 applies.
        private static byte NormalizePoints(byte points) => points == 0 ? (byte)1 : points;
    }
}
