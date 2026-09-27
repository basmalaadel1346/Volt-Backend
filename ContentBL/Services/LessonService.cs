using ContentBL.DTOs;
using ContentBL.Interfaces;
using ContentDA.Entities;
using ContentDA.Interfaces;
using Microsoft.EntityFrameworkCore;
using Shared.Common.Abstractions;
using Shared.Common.Results;

namespace ContentBL.Services;

public class LessonService : ILessonService
{
    private readonly ILessonRepository _lessonRepository;
    private readonly ILessonContentRepository _lessonContentRepository;
    private readonly ILevelRepository _levelRepository;
    private readonly ILearningProgressRepository _learningProgressRepository;
    private readonly IImageStorageService _imageStorageService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _dateTimeProvider;

    public LessonService(
        ILessonRepository lessonRepository,
        ILessonContentRepository lessonContentRepository,
        ILevelRepository levelRepository,
        ILearningProgressRepository learningProgressRepository,
        IImageStorageService imageStorageService,
        IUnitOfWork unitOfWork,
        IDateTimeProvider dateTimeProvider)
    {
        _lessonRepository = lessonRepository;
        _lessonContentRepository = lessonContentRepository;
        _levelRepository = levelRepository;
        _learningProgressRepository = learningProgressRepository;
        _imageStorageService = imageStorageService;
        _unitOfWork = unitOfWork;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<List<LessonSummaryResponse>>> GetByLevelIdAsync(int levelId, bool isAdmin, CancellationToken ct = default)
    {
        var lessons = await _lessonRepository.GetByLevelIdAsync(levelId, ct);

        // Parent/Child مايشوفوش غير الدروس المنشورة - الدروس تحت المراجعة يشوفها الأدمن بس
        if (!isAdmin)
            lessons = lessons.Where(l => l.IsPublished).ToList();

        return Result<List<LessonSummaryResponse>>.Success(lessons.Select(ToSummary).ToList());
    }

    public async Task<Result<List<PublishedLessonResponse>>> GetPublishedHomeAsync(Guid userId, CancellationToken ct = default)
    {
        var lessons = await _lessonRepository.GetAllPublishedOrderedAsync(ct);
        var completedIds = (await _learningProgressRepository.GetCompletedLessonIdsAsync(userId, ct)).ToHashSet();

        var response = new List<PublishedLessonResponse>();
        var currentAssigned = false; // أول درس لسه مخلّصهوش في التسلسل بياخد inProgress، والباقي بعده locked

        foreach (var lesson in lessons)
        {
            string status;
            if (completedIds.Contains(lesson.Id))
            {
                status = "completed";
            }
            else if (!currentAssigned)
            {
                status = "inProgress";
                currentAssigned = true;
            }
            else
            {
                status = "locked";
            }

            var isFirstLevelLesson = lesson.SortOrder == 1; // أول درس داخل المستوى بتاعه

            response.Add(new PublishedLessonResponse(
                lesson.Id, lesson.Level.Title, lesson.Title,
                isFirstLevelLesson, ToCamelCase(lesson.LessonType.Name), status));
        }

        return Result<List<PublishedLessonResponse>>.Success(response);
    }

    public async Task<Result<LessonDetailResponse>> GetDetailAsync(int id, bool isAdmin, CancellationToken ct = default)
    {
        var lesson = await _lessonRepository.GetWithContentsAsync(id, ct);

        // نفس رسالة "غير موجود" لو الدرس مش منشور ومستخدم مش Admin - عشان محدش يقدر يفرّق
        // بين "الدرس مش موجود أصلاً" و"الدرس موجود بس مش منشور لسه" (منع أي تسريب معلومة)
        if (lesson is null || (!isAdmin && !lesson.IsPublished))
            return Result<LessonDetailResponse>.Failure("الدرس غير موجود");

        var contents = lesson.LessonContents
            .Select(c => new LessonContentResponse(c.Id, c.LessonId, c.ContentTypeId, c.ContentType.Name, c.Content, c.MediaUrl, c.SortOrder))
            .ToList();

        var response = new LessonDetailResponse(
    lesson.Id,
    lesson.LevelId,
    lesson.Title,
    lesson.Description,
    lesson.SortOrder,
    lesson.IsPublished,
    lesson.LessonType.Name,
    lesson.CreatedAt,
    contents);
        return Result<LessonDetailResponse>.Success(response);
    }

    public async Task<Result<LessonSummaryResponse>> CreateAsync(CreateLessonRequest request, CancellationToken ct = default)
    {
        if (await _levelRepository.GetByIdAsync(request.LevelId, ct) is null)
            return Result<LessonSummaryResponse>.Failure("المستوى المحدد غير موجود");

        var nextOrder = await _lessonRepository.GetMaxSortOrderAsync(request.LevelId, ct) + 1;

        var lesson = new Lesson
        {
            LevelId = request.LevelId,
            Title = request.Title,
            Description = request.Description,
            SortOrder = nextOrder,
            IsPublished = false,
            CreatedAt = _dateTimeProvider.UtcNow
        };

        await _lessonRepository.AddAsync(lesson, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<LessonSummaryResponse>.Success(ToSummary(lesson));
    }

    public async Task<Result<LessonSummaryResponse>> UpdateAsync(int id, UpdateLessonRequest request, CancellationToken ct = default)
    {
        var lesson = await _lessonRepository.GetByIdAsync(id, ct);
        if (lesson is null)
            return Result<LessonSummaryResponse>.Failure("الدرس غير موجود");

        if (await _levelRepository.GetByIdAsync(request.LevelId, ct) is null)
            return Result<LessonSummaryResponse>.Failure("المستوى المحدد غير موجود");

        lesson.Title = request.Title;
        lesson.Description = request.Description;

        // لو الدرس بيتنقل لمستوى تاني، مالوش معنى يفضل مقاسم في نفس رقم الترتيب القديم
        // (ممكن يتكرر مع درس تاني في المستوى الجديد) - فبنديله ترتيب جديد في نهاية المستوى الجديد
        if (lesson.LevelId != request.LevelId)
        {
            lesson.SortOrder = await _lessonRepository.GetMaxSortOrderAsync(request.LevelId, ct) + 1;
            lesson.LevelId = request.LevelId;
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return Result<LessonSummaryResponse>.Success(ToSummary(lesson));
    }

    public async Task<Result<LessonSummaryResponse>> SetPublishedAsync(int id, bool isPublished, CancellationToken ct = default)
    {
        var lesson = await _lessonRepository.GetByIdAsync(id, ct);
        if (lesson is null)
            return Result<LessonSummaryResponse>.Failure("الدرس غير موجود");

        lesson.IsPublished = isPublished;
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<LessonSummaryResponse>.Success(ToSummary(lesson));
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken ct = default)
    {
        var lesson = await _lessonRepository.GetByIdAsync(id, ct);
        if (lesson is null)
            return Result.Failure("الدرس غير موجود");

        var contents = await _lessonContentRepository.GetByLessonIdAsync(id, ct);
        var mediaUrlsToDelete = contents.Where(c => c.MediaUrl is not null).Select(c => c.MediaUrl!).ToList();

        _lessonContentRepository.RemoveRange(contents);
        _lessonRepository.Remove(lesson);

        try
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // فيه مستخدمين خلّصوا الدرس ده بالفعل (LearningProgress مربوطة بيه بـ FK Restrict) -
            // من غير الـ Catch ده كانت هتطلع 500 خام بدل رسالة واضحة
            return Result.Failure("مينفعش تمسح الدرس ده لأن فيه مستخدمين خلّصوه بالفعل");
        }

        // نمسح الصور الفعلية من السيرفر بعد ما اتأكدنا إن المسح من الداتابيز نجح فعلًا -
        // لو مسحناها قبل كده وفشل الـ Save، كنا هنفقد الصور من غير ما نمسح السجل فعليًا
        foreach (var mediaUrl in mediaUrlsToDelete)
            _imageStorageService.DeleteImage(mediaUrl);

        return Result.Success();
    }

    public async Task<Result> SwapOrderAsync(SwapLessonsOrderRequest request, CancellationToken ct = default)
    {
        if (request.FirstLessonId == request.SecondLessonId)
            return Result.Failure("مينفعش تبدل ترتيب الدرس بنفسه");

        var first = await _lessonRepository.GetByIdAsync(request.FirstLessonId, ct);
        var second = await _lessonRepository.GetByIdAsync(request.SecondLessonId, ct);

        if (first is null || second is null)
            return Result.Failure("واحد من الدرسين غير موجود");

        if (first.LevelId != second.LevelId)
            return Result.Failure("مينفعش تبدل ترتيب درسين من مستويين مختلفين");

        (first.SortOrder, second.SortOrder) = (second.SortOrder, first.SortOrder);

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static LessonSummaryResponse ToSummary(Lesson lesson) => new(
        lesson.Id,
        lesson.LevelId,
        lesson.Title,
        lesson.Description,
        lesson.SortOrder,
        lesson.IsPublished,
        lesson.LessonType.Name,
        lesson.CreatedAt);
    // جدول LessonTypes مخزّن فيه القيم بـ PascalCase ("Lesson", "FinalLevelQuiz") لأنه Lookup Table
    // إداري، لكن الفلاتر متفقة على camelCase ("lesson", "finalLevelQuiz") في الـ API. بدل ما نربط
    // الكود بحساسية أحرف الداتابيز (لو اتغيّرت أو اتضاف نوع جديد هيبوظ)، بنحوّل أول حرف لصغير هنا
    // بشكل عام يشتغل صح مع أي قيمة PascalCase حالية أو مستقبلية.
    private static string ToCamelCase(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToLowerInvariant(value[0]) + value[1..];
}
