using ContentBL.DTOs;
using ContentBL.Interfaces;
using ContentDA.Entities;
using ContentDA.Interfaces;
using Shared.Common.Results;

namespace ContentBL.Services;

public class LessonContentService : ILessonContentService
{
    private readonly ILessonContentRepository _contentRepository;
    private readonly ILessonRepository _lessonRepository;
    private readonly IContentTypeRepository _contentTypeRepository;
    private readonly IImageStorageService _imageStorageService;
    private readonly IUnitOfWork _unitOfWork;

    public LessonContentService(
        ILessonContentRepository contentRepository,
        ILessonRepository lessonRepository,
        IContentTypeRepository contentTypeRepository,
        IImageStorageService imageStorageService,
        IUnitOfWork unitOfWork)
    {
        _contentRepository = contentRepository;
        _lessonRepository = lessonRepository;
        _contentTypeRepository = contentTypeRepository;
        _imageStorageService = imageStorageService;
        _unitOfWork = unitOfWork;
    }

    private static bool HasBody(string? content, string? mediaUrl) =>
        !string.IsNullOrWhiteSpace(content) || !string.IsNullOrWhiteSpace(mediaUrl);

    public async Task<Result<LessonContentResponse>> CreateAsync(int lessonId, CreateLessonContentRequest request, CancellationToken ct = default)
    {
        if (await _lessonRepository.GetByIdAsync(lessonId, ct) is null)
            return Result<LessonContentResponse>.Failure("الدرس غير موجود");

        if (!await _contentTypeRepository.ExistsAsync(request.ContentTypeId, ct))
            return Result<LessonContentResponse>.Failure("نوع المحتوى غير موجود");

        if (!HasBody(request.Content, request.MediaUrl))
            return Result<LessonContentResponse>.Failure("لازم يكون في نص أو صورة على الأقل");

        var nextOrder = await _contentRepository.GetMaxSortOrderAsync(lessonId, ct) + 1;

        var content = new LessonContent
        {
            LessonId = lessonId,
            ContentTypeId = request.ContentTypeId,
            Content = request.Content,
            MediaUrl = request.MediaUrl,
            SortOrder = nextOrder
        };

        await _contentRepository.AddAsync(content, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return await ToResponseAsync(content, ct);
    }

    public async Task<Result<LessonContentResponse>> UpdateAsync(int contentId, UpdateLessonContentRequest request, CancellationToken ct = default)
    {
        var content = await _contentRepository.GetByIdAsync(contentId, ct);
        if (content is null)
            return Result<LessonContentResponse>.Failure("عنصر المحتوى غير موجود");

        if (!await _contentTypeRepository.ExistsAsync(request.ContentTypeId, ct))
            return Result<LessonContentResponse>.Failure("نوع المحتوى غير موجود");

        if (!HasBody(request.Content, request.MediaUrl))
            return Result<LessonContentResponse>.Failure("لازم يكون في نص أو صورة على الأقل");

        if (content.MediaUrl != request.MediaUrl)
            _imageStorageService.DeleteImage(content.MediaUrl);

        // الـ SortOrder مايتلمسش هنا خالص - بيتغيّر بس عن طريق SwapOrderAsync
        content.ContentTypeId = request.ContentTypeId;
        content.Content = request.Content;
        content.MediaUrl = request.MediaUrl;

        await _unitOfWork.SaveChangesAsync(ct);
        return await ToResponseAsync(content, ct);
    }

    public async Task<Result> DeleteAsync(int contentId, CancellationToken ct = default)
    {
        var content = await _contentRepository.GetByIdAsync(contentId, ct);
        if (content is null)
            return Result.Failure("عنصر المحتوى غير موجود");

        _imageStorageService.DeleteImage(content.MediaUrl);
        _contentRepository.Remove(content);

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> SwapOrderAsync(SwapLessonContentsOrderRequest request, CancellationToken ct = default)
    {
        if (request.FirstContentId == request.SecondContentId)
            return Result.Failure("مينفعش تبدل ترتيب العنصر بنفسه");

        var first = await _contentRepository.GetByIdAsync(request.FirstContentId, ct);
        var second = await _contentRepository.GetByIdAsync(request.SecondContentId, ct);

        if (first is null || second is null)
            return Result.Failure("واحد من العنصرين غير موجود");

        if (first.LessonId != second.LessonId)
            return Result.Failure("مينفعش تبدل ترتيب عنصرين من درسين مختلفين");

        (first.SortOrder, second.SortOrder) = (second.SortOrder, first.SortOrder);

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<Result<LessonContentResponse>> ToResponseAsync(LessonContent content, CancellationToken ct)
    {
        var types = await _contentTypeRepository.GetAllAsync(ct);
        var typeName = types.First(t => t.Id == content.ContentTypeId).Name;

        return Result<LessonContentResponse>.Success(new LessonContentResponse(
            content.Id, content.LessonId, content.ContentTypeId, typeName,
            content.Content, content.MediaUrl, content.SortOrder));
    }
}
