using ContentBL.DTOs;
using Shared.Common.Results;

namespace ContentBL.Interfaces;

public interface ILevelService
{
    Task<Result<List<LevelResponse>>> GetAllAsync(CancellationToken ct = default);
    Task<Result<LevelResponse>> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Result<LevelResponse>> CreateAsync(CreateLevelRequest request, CancellationToken ct = default);
    Task<Result<LevelResponse>> UpdateAsync(int id, UpdateLevelRequest request, CancellationToken ct = default);
    Task<Result> DeleteAsync(int id, CancellationToken ct = default);
    Task<Result> SwapOrderAsync(SwapLevelsOrderRequest request, CancellationToken ct = default);
}
