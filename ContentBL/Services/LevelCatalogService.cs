using ContentDA.Interfaces;
using Shared.Content;

namespace ContentBL.Services;

// [Assessment-Placement] Implements the Shared contract ILevelCatalog so the
// placement test can order levels and resolve the level it places a learner at,
// without referencing ContentBL. Read-only over the existing Levels repository;
// no Content table or entity was added or changed. Do not extend beyond this list.
//
// بيدّي المودلات التانية (زي الـ Assessment) قائمة المستويات بالترتيب، من غير ما يعملوا
// Reference لمودل الـ Content كله. بيستخدمه اختبار تحديد المستوى.
public class LevelCatalogService : ILevelCatalog
{
    private readonly ILevelRepository _levelRepository;

    public LevelCatalogService(ILevelRepository levelRepository) => _levelRepository = levelRepository;

    public async Task<IReadOnlyList<LevelSummary>> GetLevelsInOrderAsync(CancellationToken cancellationToken = default)
    {
        // GetAllAsync بترجعهم مترتبين بالـ Order أصلًا
        var levels = await _levelRepository.GetAllAsync(cancellationToken);
        return levels.Select(l => new LevelSummary(l.Id, l.Title, l.Order)).ToList();
    }
}
