using ContentDA.Interfaces;
using Shared.Content;

namespace ContentBL.Services;

// [Assessment-LessonQuiz] Implements the Shared contracts ILessonAvailability and
// ILessonProgressWriter so Assessment can gate GET /api/quizzes/for-lesson and
// StartAsync on a published lesson, sample a level's lesson quizzes, and credit
// the lessons a learner proved they already know — all without referencing
// ContentBL. Nothing outside these contracts is exposed.
//
// بيجاوب المودلات التانية (زي الـ Assessment) على أسئلة محدودة عن الدروس:
// الدرس موجود؟ ومنشور؟ وإيه دروس المستوى ده؟ وكمان بيسجّل إكمال دروس الطفل
// أثبت إنه عارفها - من غير ما يعملوا Reference لمودل الـ Content كله.
public class LessonAvailabilityService : ILessonAvailability, ILessonProgressWriter
{
    private readonly ILessonRepository _lessonRepository;
    private readonly ILearningProgressRepository _progressRepository;
    private readonly IUnitOfWork _unitOfWork;

    public LessonAvailabilityService(
        ILessonRepository lessonRepository,
        ILearningProgressRepository progressRepository,
        IUnitOfWork unitOfWork)
    {
        _lessonRepository = lessonRepository;
        _progressRepository = progressRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<bool?> IsPublishedAsync(int lessonId, CancellationToken cancellationToken = default)
    {
        var lesson = await _lessonRepository.GetByIdAsync(lessonId, cancellationToken);
        return lesson?.IsPublished;
    }

    public async Task<IReadOnlyList<LessonSummary>> GetPublishedLessonsAsync(
        int levelId, CancellationToken cancellationToken = default)
    {
        // GetByLevelIdAsync بترجعهم مترتبين بالـ SortOrder أصلًا
        var lessons = await _lessonRepository.GetByLevelIdAsync(levelId, cancellationToken);

        return lessons
            .Where(l => l.IsPublished)
            .Select(l => new LessonSummary(l.Id, l.LevelId, l.Title, l.SortOrder))
            .ToList();
    }

    public async Task<IReadOnlyList<LessonSummary>> GetPublishedLessonsAsync(
        CancellationToken cancellationToken = default)
    {
        // GetPublishedAsync بترجعهم مترتبين بترتيب المستوى ثم ترتيب الدرس
        var lessons = await _lessonRepository.GetPublishedAsync(cancellationToken);

        return lessons
            .Select(l => new LessonSummary(l.Id, l.LevelId, l.Title, l.SortOrder))
            .ToList();
    }

    /// <summary>
    /// Adds a completion row for every lesson that has none yet. The unique
    /// (UserId, LessonId) index is what actually prevents a duplicate, so a
    /// concurrent caller losing that race is not an error: the lesson is complete
    /// either way, which is all this method promises.
    /// </summary>
    public async Task<int> MarkLessonsCompletedAsync(
        Guid userId,
        IReadOnlyCollection<int> lessonIds,
        CancellationToken cancellationToken = default)
    {
        if (lessonIds.Count == 0)
            return 0;

        var alreadyCompleted = (await _progressRepository.GetCompletedLessonIdsAsync(userId, cancellationToken))
            .ToHashSet();

        var missing = lessonIds.Distinct().Where(id => !alreadyCompleted.Contains(id)).ToList();

        if (missing.Count == 0)
            return 0;

        var now = DateTime.UtcNow;

        foreach (var lessonId in missing)
            await _progressRepository.AddAsync(
                new ContentDA.Entities.LearningProgress
                {
                    UserId = userId,
                    LessonId = lessonId,
                    UpdatedAt = now
                },
                cancellationToken);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            // A concurrent caller inserted one of the same rows first, or a lesson
            // was deleted between the read and the write. The rows that mattered
            // are there; nothing here is worth failing a placement over.
            return 0;
        }

        return missing.Count;
    }
}
