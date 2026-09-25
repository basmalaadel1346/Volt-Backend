using ContentBL.DTOs;
using Shared.Common.Results;

namespace ContentBL.Interfaces;

public interface IContentTypeService
{
    Task<Result<List<ContentTypeResponse>>> GetAllAsync(CancellationToken ct = default);
}
