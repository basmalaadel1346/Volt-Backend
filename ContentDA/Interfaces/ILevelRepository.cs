using ContentDA.Entities;

namespace ContentDA.Interfaces;

public interface ILevelRepository
{
    Task<List<Level>> GetAllAsync(CancellationToken ct = default);
    Task<Level?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>أعلى قيمة Order موجودة حاليًا (0 لو مفيش مستويات خالص).</summary>
    Task<int> GetMaxOrderAsync(CancellationToken ct = default);

    Task AddAsync(Level level, CancellationToken ct = default);
    void Remove(Level level);
}
