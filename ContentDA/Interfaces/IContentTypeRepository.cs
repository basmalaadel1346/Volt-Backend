using ContentDA.Entities;

namespace ContentDA.Interfaces;

public interface IContentTypeRepository
{
    Task<List<ContentType>> GetAllAsync(CancellationToken ct = default);
    Task<bool> ExistsAsync(int id, CancellationToken ct = default);
}
