using ContentBL.DTOs;
using ContentBL.Interfaces;
using ContentDA.Interfaces;
using Shared.Common.Results;

namespace ContentBL.Services;

public class ContentTypeService : IContentTypeService
{
    private readonly IContentTypeRepository _repository;
    public ContentTypeService(IContentTypeRepository repository) => _repository = repository;

    public async Task<Result<List<ContentTypeResponse>>> GetAllAsync(CancellationToken ct = default)
    {
        var types = await _repository.GetAllAsync(ct);
        var response = types.Select(t => new ContentTypeResponse(t.Id, t.Name)).ToList();
        return Result<List<ContentTypeResponse>>.Success(response);
    }
}
