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
    // for admin use only; children do not create or update options, they only pick them
    public class QuestionOptionService : IQuestionOptionServiceForAdmin
    {
        private static readonly Expression<Func<QuestionOption, AdminQuestionOptionResponseDto>> ProjectToResponse =
            option => new AdminQuestionOptionResponseDto
            {
                Id = option.Id,
                OptionText = option.OptionText,
                ImageUrl = option.ImageUrl,
                ImageDescription = option.ImageDescription,
                IsCorrect = option.IsCorrect,
                DisplayOrder = option.DisplayOrder
            };

        private readonly AssessmentDbContext _db;

        public QuestionOptionService(AssessmentDbContext db) => _db = db;

        public async Task<IReadOnlyList<AdminQuestionOptionResponseDto>> GetByQuestionIdAsync(int questionId, CancellationToken cancellationToken = default)
        {
            var questionExists = await _db.Questions.AsNoTracking().AnyAsync(q => q.Id == questionId, cancellationToken: cancellationToken);
            if (!questionExists)
                throw new KeyNotFoundException($"السؤال رقم {questionId} غير موجود");

            return await _db.QuestionOptions
                .AsNoTracking()
                .Where(o => o.QuestionId == questionId)
                .OrderBy(o => o.DisplayOrder)
                .Select(ProjectToResponse)
                .ToListAsync(cancellationToken: cancellationToken);
        }

        public async Task<AdminQuestionOptionResponseDto> CreateOptionAsync(CreateQuestionOptionDto request, CancellationToken cancellationToken = default)
        {
            var questionExists = await _db.Questions.AsNoTracking().AnyAsync(q => q.Id == request.QuestionId, cancellationToken: cancellationToken);
            if (!questionExists)
                throw new KeyNotFoundException($"السؤال رقم {request.QuestionId} غير موجود");

            var (optionText, imageUrl, imageDescription) =
                NormalizeContent(request.OptionText, request.ImageUrl, request.ImageDescription);

            await EnsureQuestionAcceptsOptionsAsync(request.QuestionId, cancellationToken);

            await EnsureDisplayOrderIsFreeAsync(request.QuestionId, request.DisplayOrder, excludingOptionId: null, cancellationToken: cancellationToken);

            if (request.IsCorrect)
                await EnsureNoOtherCorrectOptionAsync(request.QuestionId, excludingOptionId: null, cancellationToken: cancellationToken);

            var option = new QuestionOption
            {
                QuestionId = request.QuestionId,
                OptionText = optionText,
                ImageUrl = imageUrl,
                ImageDescription = imageDescription,
                IsCorrect = request.IsCorrect,
                DisplayOrder = request.DisplayOrder
                // CreatedAt is filled by the sysutcdatetime() column default.
            };

            _db.QuestionOptions.Add(option);
            await SaveWithOptionConflictAsync(request.QuestionId, request.DisplayOrder, cancellationToken);

            return ToResponse(option);
        }

        public async Task<AdminQuestionOptionResponseDto> UpdateOptionAsync(
     int optionId,
     UpdateQuestionOptionDto request, CancellationToken cancellationToken = default)
        {
            var option = await _db.QuestionOptions
                .FirstOrDefaultAsync(o => o.Id == optionId, cancellationToken: cancellationToken)
                ?? throw new KeyNotFoundException($"الاختيار رقم {optionId} غير موجود");

            var (optionText, imageUrl, imageDescription) =
                NormalizeContent(request.OptionText, request.ImageUrl, request.ImageDescription);

            if (option.DisplayOrder != request.DisplayOrder)
                await EnsureDisplayOrderIsFreeAsync(
                    option.QuestionId,
                    request.DisplayOrder,
                    excludingOptionId: optionId,
                    cancellationToken: cancellationToken);

            if (request.IsCorrect && !option.IsCorrect)
                await EnsureNoOtherCorrectOptionAsync(
                    option.QuestionId,
                    excludingOptionId: optionId,
                    cancellationToken: cancellationToken);

            var questionIsActive = await _db.Questions
                .AsNoTracking()
                .Where(q => q.Id == option.QuestionId)
                .Select(q => q.IsActive)
                .FirstAsync(cancellationToken: cancellationToken);

            if (questionIsActive && option.IsCorrect && !request.IsCorrect)
            {
                var correctOptionsCount = await _db.QuestionOptions
                    .AsNoTracking()
                    .CountAsync(o =>
                        o.QuestionId == option.QuestionId &&
                        o.IsCorrect,
                        cancellationToken: cancellationToken);

                if (correctOptionsCount == 1)
                    throw new BusinessRuleException(
                        $"لا يمكن إلغاء الإجابة الصحيحة الوحيدة من السؤال رقم {option.QuestionId} وهو مفعّل");
            }

            option.OptionText = optionText;
            option.ImageUrl = imageUrl;
            option.ImageDescription = imageDescription;
            option.IsCorrect = request.IsCorrect;
            option.DisplayOrder = request.DisplayOrder;

            await SaveWithOptionConflictAsync(option.QuestionId, request.DisplayOrder, cancellationToken);

            return ToResponse(option);
        }
        /// <summary>
        /// Moves the correct answer of a question to another of its options, in one
        /// atomic step.
        ///
        /// The two writes have to happen together: with the old option still
        /// correct, setting the new one violates
        /// UQ_QuestionOptions_OneCorrectPerQuestion; with the old one already
        /// cleared, the question has no correct answer and is unusable. Doing both
        /// inside a transaction is what lets the admin change their mind in one
        /// request instead of four, and is why a lost connection can no longer
        /// strand a published question with a broken answer key.
        ///
        /// Nothing about the question's ACTIVE state changes: it is never
        /// deactivated on the way, so the change is invisible to children beyond
        /// the answer key itself. Attempts already started are unaffected — they
        /// were graded against the key frozen in their own snapshot.
        /// </summary>
        public async Task<IReadOnlyList<AdminQuestionOptionResponseDto>> SetCorrectOptionAsync(
            int questionId,
            SetCorrectOptionDto request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var question = await _db.Questions
                .AsNoTracking()
                .Where(q => q.Id == questionId)
                .Select(q => new { q.QuestionType })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException($"السؤال رقم {questionId} غير موجود");

            if (!QuestionTypes.UsesOptions(question.QuestionType))
                throw new BusinessRuleException(
                    $"السؤال رقم {questionId} سؤال مقالي وليس له إجابة صحيحة");

            var options = await _db.QuestionOptions
                .Where(o => o.QuestionId == questionId)
                .ToListAsync(cancellationToken);

            var target = options.FirstOrDefault(o => o.Id == request.CorrectOptionId)
                ?? throw new KeyNotFoundException(
                    $"الاختيار رقم {request.CorrectOptionId} لا يخص السؤال رقم {questionId}");

            // Already the only correct one: nothing to write, and saying so with a
            // 200 keeps the endpoint idempotent under a client retry.
            if (target.IsCorrect && options.Count(o => o.IsCorrect) == 1)
                return Project(options);

            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

            // Clear first, in the same transaction, so the unique index never sees
            // two correct options even for an instant.
            foreach (var option in options.Where(o => o.IsCorrect && o.Id != target.Id))
                option.IsCorrect = false;

            target.IsCorrect = true;

            await SaveWithOptionConflictAsync(questionId, target.DisplayOrder, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Project(options);
        }

        /// <summary>
        /// Rewrites the DisplayOrder of a question's options from an absolute
        /// ordered list of ids — idempotent, unlike the pairwise swap it replaces.
        ///
        /// The whole set is renumbered 1..n inside one transaction, going through a
        /// negative staging pass first because
        /// UQ_QuestionOptions_QuestionId_DisplayOrder would otherwise be violated
        /// mid-update by any order that is a rotation of the current one.
        /// </summary>
        public async Task<IReadOnlyList<AdminQuestionOptionResponseDto>> ReorderAsync(
            int questionId,
            IReadOnlyList<int> orderedOptionIds,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(orderedOptionIds);

            var questionExists = await _db.Questions
                .AsNoTracking()
                .AnyAsync(q => q.Id == questionId, cancellationToken);

            if (!questionExists)
                throw new KeyNotFoundException($"السؤال رقم {questionId} غير موجود");

            var options = await _db.QuestionOptions
                .Where(o => o.QuestionId == questionId)
                .ToListAsync(cancellationToken);

            DisplayOrdering.EnsureCoversExactly(
                orderedOptionIds,
                options.Select(o => o.Id),
                "الاختيارات",
                $"السؤال رقم {questionId}");

            var optionsById = options.ToDictionary(o => o.Id);

            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

            // Pass 1: park every row on a value no real row can hold, so the unique
            // index cannot be tripped by an intermediate state.
            for (var i = 0; i < orderedOptionIds.Count; i++)
                optionsById[orderedOptionIds[i]].DisplayOrder = (short)-(i + 1);

            await _db.SaveChangesAsync(cancellationToken);

            // Pass 2: the order the admin asked for.
            for (var i = 0; i < orderedOptionIds.Count; i++)
                optionsById[orderedOptionIds[i]].DisplayOrder = (short)(i + 1);

            await SaveWithOptionConflictAsync(questionId, 0, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Project(options);
        }

        private static List<AdminQuestionOptionResponseDto> Project(IEnumerable<QuestionOption> options) =>
            options.OrderBy(o => o.DisplayOrder).Select(ToResponse).ToList();

        public async Task DeleteOptionAsync(int optionId, CancellationToken cancellationToken = default)
        {
            var option = await _db.QuestionOptions
                .FirstOrDefaultAsync(o => o.Id == optionId, cancellationToken: cancellationToken)
                ?? throw new KeyNotFoundException($"الاختيار رقم {optionId} غير موجود");

            var isReferenced = await _db.QuizAttemptMistakes
                .AsNoTracking()
                .AnyAsync(m => m.SelectedOptionId == optionId, cancellationToken: cancellationToken);

            if (isReferenced)
                throw new BusinessRuleException(
                    $"لا يمكن حذف الاختيار رقم {optionId} لأنه مستخدم في محاولات سابقة");

            // FK_QuizAttemptQuestions_QuestionId_CorrectOptionId would block this
            // anyway; checking here turns it into a clear business message.
            var isSnapshottedAnswerKey = await _db.QuizAttemptQuestions
                .AsNoTracking()
                .AnyAsync(aq => aq.CorrectOptionId == optionId, cancellationToken: cancellationToken);

            if (isSnapshottedAnswerKey)
                throw new BusinessRuleException(
                    $"لا يمكن حذف الاختيار رقم {optionId} لأنه الإجابة الصحيحة المسجّلة في محاولات سابقة");

            var question = await _db.Questions
                .AsNoTracking()
                .Where(q => q.Id == option.QuestionId)
                .Select(q => new { q.IsActive, OptionCount = q.QuestionOptions.Count() })
                .FirstAsync(cancellationToken: cancellationToken);

            // A published question must stay answerable: MultipleChoice needs at
            // least two options and TrueFalse exactly two — the rule SetActiveAsync
            // enforced when it was published.
            if (question.IsActive && question.OptionCount <= 2)
                throw new BusinessRuleException(
                    $"لا يمكن حذف الاختيار رقم {optionId}: السؤال رقم {option.QuestionId} مفعّل ويحتاج إلى اختيارين على الأقل");

            var questionIsActive = question.IsActive;

            if (questionIsActive && option.IsCorrect)
            {
                var correctOptionsCount = await _db.QuestionOptions
                    .AsNoTracking()
                    .CountAsync(o =>
                        o.QuestionId == option.QuestionId &&
                        o.IsCorrect,
                        cancellationToken: cancellationToken);

                if (correctOptionsCount == 1)
                    throw new BusinessRuleException(
                        $"لا يمكن حذف الإجابة الصحيحة الوحيدة من السؤال رقم {option.QuestionId} وهو مفعّل");
            }

            _db.QuestionOptions.Remove(option);
            await _db.SaveChangesAsync(cancellationToken);
        }

        // The EnsureXxx pre-checks below are friendly validation only; two admins
        // can pass them concurrently. UQ_QuestionOptions_QuestionId_DisplayOrder
        // and UQ_QuestionOptions_OneCorrectPerQuestion are the real protection,
        // and this turns losing either race into a 409 rather than a 500.
        private async Task SaveWithOptionConflictAsync(
            int questionId, short displayOrder, CancellationToken cancellationToken)
        {
            try
            {
                await _db.SaveChangesAsync(cancellationToken: cancellationToken);
            }
            catch (DbUpdateException ex) when (
                ex.IsUniqueViolationOf("UQ_QuestionOptions_QuestionId_DisplayOrder"))
            {
                throw new ConflictException(
                    $"الترتيب {displayOrder} مستخدم بالفعل في السؤال رقم {questionId}", ex);
            }
            catch (DbUpdateException ex) when (
                ex.IsUniqueViolationOf("UQ_QuestionOptions_OneCorrectPerQuestion"))
            {
                throw new ConflictException(
                    $"السؤال رقم {questionId} له إجابة صحيحة بالفعل", ex);
            }
        }

        // Mirrors UQ_QuestionOptions_QuestionId_DisplayOrder.
        private async Task EnsureDisplayOrderIsFreeAsync(int questionId, short displayOrder, int? excludingOptionId, CancellationToken cancellationToken = default)
        {
            var taken = await _db.QuestionOptions
                .AsNoTracking()
                .AnyAsync(o => o.QuestionId == questionId
                            && o.DisplayOrder == displayOrder
                            && (excludingOptionId == null || o.Id != excludingOptionId.Value), cancellationToken: cancellationToken);

            if (taken)
                throw new BusinessRuleException(
                    $"الترتيب {displayOrder} مستخدم بالفعل في السؤال رقم {questionId}");
        }

        // Mirrors the filtered unique index UQ_QuestionOptions_OneCorrectPerQuestion.
        private async Task EnsureNoOtherCorrectOptionAsync(int questionId, int? excludingOptionId, CancellationToken cancellationToken = default)
        {
            var alreadyHasCorrect = await _db.QuestionOptions
                .AsNoTracking()
                .AnyAsync(o => o.QuestionId == questionId
                            && o.IsCorrect
                            && (excludingOptionId == null || o.Id != excludingOptionId.Value), cancellationToken: cancellationToken);

            if (alreadyHasCorrect)
                throw new BusinessRuleException(
                    $"السؤال رقم {questionId} له إجابة صحيحة بالفعل");
        }

        // Mirrors the NVARCHAR(1000) ImageDescription column.
        private const int MaxImageDescriptionLength = 1000;

        /// <summary>
        /// An option may be text-only, image-only, or both — but never neither.
        /// Mirrors CK_QuestionOptions_TextOrImage and
        /// CK_QuestionOptions_ImageHasDescription so the admin gets a clear message
        /// instead of a raw check-constraint violation.
        /// </summary>
        private static (string? OptionText, string? ImageUrl, string? ImageDescription) NormalizeContent(
            string? optionText, string? imageUrl, string? imageDescription)
        {
            var text = string.IsNullOrWhiteSpace(optionText) ? null : optionText.Trim();
            var image = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl.Trim();

            // A description without an image is dropped, so it can never outlive a
            // removed image and describe something the child does not see.
            var description = image is null || string.IsNullOrWhiteSpace(imageDescription)
                ? null
                : imageDescription.Trim();

            if (text is null && image is null)
                throw new ArgumentException(
                    "الاختيار يجب أن يحتوي على نص أو صورة على الأقل", nameof(optionText));

            // Required even when the option also has text: the AI never looks at
            // images, and the text may only label the picture ("A", "B"), so
            // without a description the AI cannot tell what the child picked.
            if (image is not null && description is null)
                throw new ArgumentException(
                    "الاختيار الذي يحتوي على صورة يجب أن يحتوي على وصف للصورة حتى يتمكن النظام من تحليل إجابة الطالب",
                    nameof(imageDescription));

            if (description is not null && description.Length > MaxImageDescriptionLength)
                throw new ArgumentException(
                    $"وصف الصورة لا يتجاوز {MaxImageDescriptionLength} حرف", nameof(imageDescription));

            return (text, image, description);
        }

        /// <summary>
        /// An Essay question has no options and must never be given any, and a
        /// TrueFalse question has exactly two. Checked on every add, not only at
        /// activation — otherwise a published TrueFalse could gain a third option.
        /// </summary>
        private async Task EnsureQuestionAcceptsOptionsAsync(
            int questionId, CancellationToken cancellationToken = default)
        {
            var question = await _db.Questions
                .AsNoTracking()
                .Where(q => q.Id == questionId)
                .Select(q => new { q.QuestionType, OptionCount = q.QuestionOptions.Count() })
                .FirstAsync(cancellationToken);

            if (!QuestionTypes.UsesOptions(question.QuestionType))
                throw new BusinessRuleException(
                    $"السؤال رقم {questionId} سؤال مقالي ولا يقبل اختيارات");

            if (question.QuestionType == QuestionTypes.TrueFalse && question.OptionCount >= 2)
                throw new BusinessRuleException(
                    $"سؤال الصح والخطأ رقم {questionId} له اختياران بالفعل");
        }

        private static AdminQuestionOptionResponseDto ToResponse(QuestionOption option) => new()
        {
            Id = option.Id,
            OptionText = option.OptionText,
            ImageUrl = option.ImageUrl,
            ImageDescription = option.ImageDescription,
            IsCorrect = option.IsCorrect,
            DisplayOrder = option.DisplayOrder
        };
    }
}
