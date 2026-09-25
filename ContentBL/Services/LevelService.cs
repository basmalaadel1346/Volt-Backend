using ContentBL.DTOs;
using ContentBL.Interfaces;
using ContentDA.Entities;
using ContentDA.Interfaces;
using Microsoft.EntityFrameworkCore;
using Shared.Common.Results;

namespace ContentBL.Services;

public class LevelService : ILevelService
{
    private readonly ILevelRepository _levelRepository;
    private readonly IUnitOfWork _unitOfWork;

    public LevelService(ILevelRepository levelRepository, IUnitOfWork unitOfWork)
    {
        _levelRepository = levelRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<List<LevelResponse>>> GetAllAsync(CancellationToken ct = default)
    {
        var levels = await _levelRepository.GetAllAsync(ct);
        return Result<List<LevelResponse>>.Success(levels.Select(ToResponse).ToList());
    }

    public async Task<Result<LevelResponse>> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var level = await _levelRepository.GetByIdAsync(id, ct);
        return level is null
            ? Result<LevelResponse>.Failure("المستوى غير موجود")
            : Result<LevelResponse>.Success(ToResponse(level));
    }

    public async Task<Result<LevelResponse>> CreateAsync(CreateLevelRequest request, CancellationToken ct = default)
    {
        var nextOrder = await _levelRepository.GetMaxOrderAsync(ct) + 1;

        var level = new Level { Title = request.Title, Description = request.Description, Order = nextOrder };
        await _levelRepository.AddAsync(level, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<LevelResponse>.Success(ToResponse(level));
    }

    public async Task<Result<LevelResponse>> UpdateAsync(int id, UpdateLevelRequest request, CancellationToken ct = default)
    {
        var level = await _levelRepository.GetByIdAsync(id, ct);
        if (level is null)
            return Result<LevelResponse>.Failure("المستوى غير موجود");

        // الـ Order مايتلمسش هنا خالص - بيتغيّر بس عن طريق SwapOrderAsync
        level.Title = request.Title;
        level.Description = request.Description;

        await _unitOfWork.SaveChangesAsync(ct);
        return Result<LevelResponse>.Success(ToResponse(level));
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken ct = default)
    {
        var level = await _levelRepository.GetByIdAsync(id, ct);
        if (level is null)
            return Result.Failure("المستوى غير موجود");

        _levelRepository.Remove(level);

        try
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Result.Failure("مينفعش تمسح المستوى ده لأن فيه دروس مرتبطة بيه، امسح الدروس الأول");
        }

        return Result.Success();
    }

    public async Task<Result> SwapOrderAsync(SwapLevelsOrderRequest request, CancellationToken ct = default)
    {
        if (request.FirstLevelId == request.SecondLevelId)
            return Result.Failure("مينفعش تبدل ترتيب المستوى بنفسه");

        var first = await _levelRepository.GetByIdAsync(request.FirstLevelId, ct);
        var second = await _levelRepository.GetByIdAsync(request.SecondLevelId, ct);

        if (first is null || second is null)
            return Result.Failure("واحد من المستويين غير موجود");

        (first.Order, second.Order) = (second.Order, first.Order);

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static LevelResponse ToResponse(Level level) => new(level.Id, level.Title, level.Description, level.Order);
}
