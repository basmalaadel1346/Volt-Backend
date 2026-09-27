using GamificationBL.DTOs;

namespace GamificationBL.Interfaces;

public interface IGamificationService
{
    /// <summary>
    /// The child's wallet, streak, inventory and running boosts — everything the
    /// reward bar draws. Creates nothing: a learner who has earned nothing yet
    /// reads as an empty wallet.
    /// </summary>
    Task<LearnerGamificationDto> GetMineAsync(
        Guid userId, string? language = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// The shop, priced for this child: what each item costs, how many they own,
    /// and whether they can buy it right now.
    /// </summary>
    Task<IReadOnlyList<ShopItemDto>> GetShopAsync(
        Guid userId, string? language = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Buys one item. Fails with 400 when the child cannot afford it or is at the
    /// ownership cap, and with 409 when a concurrent purchase spent the same
    /// Sparks first — never by letting a balance go negative.
    /// </summary>
    Task<PurchaseResultDto> PurchaseAsync(
        Guid userId, int itemId, string? language = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts on an owned cosmetic (and takes off whatever was worn before).
    /// </summary>
    Task<LearnerGamificationDto> EquipAsync(
        Guid userId, int itemId, string? language = null, CancellationToken cancellationToken = default);
}
