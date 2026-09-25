using ContentBL.DTOs;
using ContentBL.Interfaces;
using ContentDA.Entities;
using ContentDA.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Assessment;
using Shared.Common.Results;
using Shared.Gamification;

namespace ContentBL.Services;

public class LearningProgressService : ILearningProgressService
{
    private readonly ILearningProgressRepository _repository;
    private readonly ILessonRepository _lessonRepository;
    private readonly ILessonQuizGate _quizGate;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILearningRewards _rewards;
    private readonly ILogger<LearningProgressService> _logger;

    public LearningProgressService(
        ILearningProgressRepository repository,
        ILessonRepository lessonRepository,
        ILessonQuizGate quizGate,
        IUnitOfWork unitOfWork,
        ILearningRewards rewards,
        ILogger<LearningProgressService> logger)
    {
        _repository = repository;
        _lessonRepository = lessonRepository;
        _quizGate = quizGate;
        _unitOfWork = unitOfWork;
        _rewards = rewards;
        _logger = logger;
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
            // خلص قبل كده: مفيش صف جديد، ومفيش مكافأة تانية. بنرجّع رصيد الشرارات
            // الحالي عشان الشاشة تفضل صح لو الطلب اتعاد.
            return Result<LessonProgressResponse>.Success(new LessonProgressResponse(
                lessonId, existing.UpdatedAt, await DescribeRewardsAsync(userId, lessonId, ct)));

        var progress = new LearningProgress
        {
            UserId = userId,
            LessonId = lessonId,
            UpdatedAt = DateTime.UtcNow
        };

        await _repository.AddAsync(progress, ct);

        try
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // إما الـ LessonId مش موجود (FK)، أو حصل Race Condition ونفس الصف اتضاف من طلب متزامن
            // (الـ Unique Constraint هي اللي بتمنع الـ Duplicate فعليًا وقت الحفظ)
            return Result<LessonProgressResponse>.Failure("الدرس غير موجود");
        }

        // المكافأة بتتحسب بعد ما الإكمال اتسجّل: الشرارات والسلسلة مالهمش لازمة لو
        // الدرس نفسه مااتسجلش، والعكس مش صحيح — عشان كده المكافأة مابتفشّلش الطلب أبدًا.
        var rewards = await GrantLessonRewardAsync(userId, lessonId, ct);

        return Result<LessonProgressResponse>.Success(
            new LessonProgressResponse(lessonId, progress.UpdatedAt, rewards));
    }

    /// <summary>
    /// بتدّي الطفل شرارات الدرس وبتمدّد السلسلة. الـ ReferenceKey بيخلي الدرس
    /// الواحد مايتحسبش مرتين مهما الطلب اتعاد.
    /// </summary>
    private async Task<RewardOutcome> GrantLessonRewardAsync(Guid userId, int lessonId, CancellationToken ct)
    {
        try
        {
            return await _rewards.RecordActivityAsync(
                new LearningActivity(userId, LearningActivityKind.LessonCompleted, $"lesson:{lessonId}"),
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Lesson {LessonId} was completed by user {UserId}, but its rewards could not be granted.",
                lessonId, userId);

            return RewardOutcome.None;
        }
    }

    /// <summary>
    /// نفس النداء بالظبط: الـ ReferenceKey اتدفع قبل كده، فالمودل بيرجّع المحفظة
    /// من غير ما يزوّد حاجة.
    /// </summary>
    private Task<RewardOutcome> DescribeRewardsAsync(Guid userId, int lessonId, CancellationToken ct) =>
        GrantLessonRewardAsync(userId, lessonId, ct);

    /// <summary>
    /// القاعدة: الطفل مايفتحش الدرس التالي غير لما ينجح في اختبار الدرس اللي قبله
    /// في نفس المستوى.
    ///
    /// الترتيب بييجي من الـ Content (SortOrder جوه المستوى)، والنجاح بييجي من مودل
    /// الـ Assessment عن طريق ILessonQuizGate - ولا واحد فيهم بيعرف التاني، فالقاعدة
    /// بتتجمّع هنا.
    ///
    /// أول درس في المستوى مفتوح دايمًا، والدرس اللي قبله لو مالوش اختبار مفعّل
    /// بيتحسب "ناجح" - مينفعش قفل الكورس كله على إدمن نسي يعمل اختبار.
    /// </summary>
    public async Task<Result<LessonAccessResponse>> GetLessonAccessAsync(
        Guid userId, int lessonId, CancellationToken ct = default)
    {
        var lesson = await _lessonRepository.GetByIdAsync(lessonId, ct);

        if (lesson is null)
            return Result<LessonAccessResponse>.Failure("الدرس غير موجود");

        var levelAccess = await BuildLevelAccessAsync(userId, lesson.LevelId, ct);

        var access = levelAccess.FirstOrDefault(a => a.LessonId == lessonId);

        return access is null
            // الدرس موجود بس مش منشور، فهو مش ضمن قائمة دروس المستوى المنشورة.
            ? Result<LessonAccessResponse>.Success(new LessonAccessResponse(
                lessonId, false, false, null, null, null,
                LessonAccessReasons.LessonNotPublished,
                "الدرس ده لسه مش متاح."))
            : Result<LessonAccessResponse>.Success(access);
    }

    public async Task<Result<List<LessonAccessResponse>>> GetLevelAccessAsync(
        Guid userId, int levelId, CancellationToken ct = default) =>
        Result<List<LessonAccessResponse>>.Success(await BuildLevelAccessAsync(userId, levelId, ct));

    private async Task<List<LessonAccessResponse>> BuildLevelAccessAsync(
        Guid userId, int levelId, CancellationToken ct)
    {
        var lessons = (await _lessonRepository.GetByLevelIdAsync(levelId, ct))
            .Where(l => l.IsPublished)
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.Id)
            .ToList();

        if (lessons.Count == 0)
            return [];

        var lessonIds = lessons.Select(l => l.Id).ToList();

        // نداءين بس مهما كان عدد الدروس: الإكمال، والنجاح في الاختبارات.
        var completed = (await _repository.GetCompletedLessonIdsAsync(userId, ct)).ToHashSet();
        var passed = await _quizGate.GetPassedLessonIdsAsync(userId, lessonIds, ct);

        var access = new List<LessonAccessResponse>(lessons.Count);

        for (var i = 0; i < lessons.Count; i++)
        {
            var lesson = lessons[i];
            var previous = i == 0 ? null : lessons[i - 1];

            // أول درس مفتوح، وبعد كده الشرط هو اختبار الدرس السابق.
            var unlocked = previous is null || passed.Contains(previous.Id);

            access.Add(new LessonAccessResponse(
                lesson.Id,
                unlocked,
                completed.Contains(lesson.Id),
                unlocked ? null : previous!.Id,
                unlocked ? null : previous!.Title,
                null,
                unlocked ? LessonAccessReasons.Unlocked : LessonAccessReasons.PreviousLessonQuizNotPassed,
                unlocked
                    ? "الدرس متاح، يلا نبدأ!"
                    : $"لازم تنجح في اختبار درس \"{previous!.Title}\" الأول عشان تفتح الدرس ده."));
        }

        return access;
    }
}
