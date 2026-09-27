using ContentBL.DTOs;
using ContentBL.Interfaces;
using ContentDA.Entities;
using ContentDA.Interfaces;
using Microsoft.EntityFrameworkCore;
using Shared.Common.Abstractions;
using Shared.Common.Results;

namespace ContentBL.Services;

public class LearningProgressService : ILearningProgressService
{
    private readonly ILearningProgressRepository _repository;
    private readonly ILessonRepository _lessonRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _dateTimeProvider;

    public LearningProgressService(
        ILearningProgressRepository repository,
        ILessonRepository lessonRepository,
        IUnitOfWork unitOfWork,
        IDateTimeProvider dateTimeProvider)
    {
        _repository = repository;
        _lessonRepository = lessonRepository;
        _unitOfWork = unitOfWork;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<List<int>>> GetCompletedLessonIdsAsync(Guid userId, CancellationToken ct = default)
    {
        var ids = await _repository.GetCompletedLessonIdsAsync(userId, ct);
        return Result<List<int>>.Success(ids);
    }

    public async Task<Result<LessonProgressStatusResponse>> GetLessonProgressAsync(Guid userId, int lessonId, CancellationToken ct = default)
    {
        var progress = await _repository.GetByUserAndLessonAsync(userId, lessonId, ct);

        // مفيش صف = لسه مخلّصش الدرس - ده رد صحيح ومتوقع، مش خطأ
        var response = new LessonProgressStatusResponse(lessonId, progress is not null, progress?.UpdatedAt);
        return Result<LessonProgressStatusResponse>.Success(response);
    }

    public async Task<Result<LessonProgressResponse>> MarkLessonCompleteAsync(Guid userId, int lessonId, CancellationToken ct = default)
    {
        var existing = await _repository.GetByUserAndLessonAsync(userId, lessonId, ct);
        if (existing is not null)
            return Result<LessonProgressResponse>.Success(new LessonProgressResponse(lessonId, existing.UpdatedAt));

        // نفس ترتيب المسار الكامل اللي شاشة الهوم بتستخدمه (كل المستويات مع بعض، Level.Order
        // ثم SortOrder) - عشان نتأكد إن الدرس ده مش "locked" فعليًا قبل ما نسجّله كمكتمل.
        // التطبيق لازم ياخد الدروس بالترتيب، فمينفعش الطفل "يقفز" لدرس قبله دروس لسه مخلصهاش.
        var orderedLessons = await _lessonRepository.GetAllPublishedOrderedAsync(ct);
        var lessonIndex = orderedLessons.FindIndex(l => l.Id == lessonId);
        if (lessonIndex == -1)
            return Result<LessonProgressResponse>.Failure("الدرس غير موجود أو غير منشور");

        var completedIds = (await _repository.GetCompletedLessonIdsAsync(userId, ct)).ToHashSet();

        // أي درس قبل الدرس ده في المسار الكامل (بغض النظر عن المستوى) لازم يكون خلص الأول
        var hasIncompletePrecedingLesson = orderedLessons
            .Take(lessonIndex)
            .Any(l => !completedIds.Contains(l.Id));

        if (hasIncompletePrecedingLesson)
            return Result<LessonProgressResponse>.Failure("لازم تخلص الدروس اللي قبل الدرس ده الأول");

        var progress = new LearningProgress
        {
            UserId = userId,
            LessonId = lessonId,
            UpdatedAt = _dateTimeProvider.UtcNow
        };

        await _repository.AddAsync(progress, ct);

        try
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // حصل Race Condition ونفس الصف اتضاف من طلب متزامن (الـ Unique Constraint هي اللي
            // بتمنع الـ Duplicate فعليًا وقت الحفظ) - الدرس نفسه اتأكد وجوده وكونه منشور فوق بالفعل
            return Result<LessonProgressResponse>.Failure("تم تسجيل إكمال هذا الدرس بالفعل");
        }

        return Result<LessonProgressResponse>.Success(new LessonProgressResponse(lessonId, progress.UpdatedAt));
    }
}
