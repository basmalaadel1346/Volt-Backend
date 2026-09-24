using GamificationBL.DTOs;
using GamificationBL.Interfaces;
using GamificationBL.Services.Constants;
using GamificationDA.Context;
using GamificationDA.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shared.Common.Abstractions;
using Shared.Common.Exceptions;

namespace GamificationBL.Services;

/// <summary>
/// The child-facing side of gamification: the wallet, the shop and the
/// inventory. Earning lives in <see cref="LearningRewardsService"/>; this is
/// where Sparks are spent.
/// </summary>
public sealed class GamificationService : IGamificationService
{
    private const string English = "en";

    private readonly GamificationDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly GamificationSettings _settings;

    public GamificationService(
        GamificationDbContext db,
        IDateTimeProvider clock,
        IOptions<GamificationSettings> settings)
    {
        _db = db;
        _clock = clock;
        _settings = settings.Value;
    }

    public async Task<LearnerGamificationDto> GetMineAsync(
        Guid userId, string? language = null, CancellationToken cancellationToken = default)
    {
        var resolved = NormalizeLanguage(language);
        var now = _clock.UtcNow;
        var today = DateOnly.FromDateTime(now);

        var wallet = await _db.LearnerWallets
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.UserId == userId, cancellationToken);

        var items = await _db.LearnerItems
            .AsNoTracking()
            .Where(i => i.UserId == userId)
            .Select(i => new OwnedItemDto
            {
                ItemId = i.ShopItemId,
                Code = i.ShopItem.Code,
                Kind = i.ShopItem.Kind,
                Name = resolved == English ? i.ShopItem.NameEn : i.ShopItem.NameAr,
                ImageUrl = i.ShopItem.ImageUrl,
                Quantity = i.Quantity,
                IsEquipped = i.IsEquipped
            })
            .ToListAsync(cancellationToken);

        var boosts = await _db.LearnerBoosts
            .AsNoTracking()
            .Where(b => b.UserId == userId && b.ExpiresAt > now)
            .OrderBy(b => b.ExpiresAt)
            .Select(b => new ActiveBoostDto
            {
                ItemId = b.ShopItemId,
                Code = b.ShopItem.Code,
                Name = resolved == English ? b.ShopItem.NameEn : b.ShopItem.NameAr,
                Multiplier = b.Multiplier,
                ExpiresAt = b.ExpiresAt
            })
            .ToListAsync(cancellationToken);

        foreach (var boost in boosts)
            boost.SecondsRemaining = (int)Math.Max((boost.ExpiresAt - now).TotalSeconds, 0);

        var streakDays = wallet?.CurrentStreakDays ?? 0;
        var activeToday = wallet?.LastActivityOn == today;
        var freezes = wallet?.StreakFreezes ?? 0;

        var milestoneEvery = _settings.EffectiveStreakMilestoneDays;
        var daysToMilestone = streakDays <= 0
            ? milestoneEvery
            : milestoneEvery - streakDays % milestoneEvery;

        return new LearnerGamificationDto
        {
            Language = resolved,
            SparksBalance = wallet?.SparksBalance ?? 0,
            LifetimeSparks = wallet?.LifetimeSparks ?? 0,
            CurrentStreakDays = streakDays,
            LongestStreakDays = wallet?.LongestStreakDays ?? 0,
            LastActivityOn = wallet?.LastActivityOn,
            ActiveToday = activeToday,
            // Today still counts if nothing has been done yet, and each freeze buys
            // one more day after that — the same arithmetic StreakRules applies.
            StreakSafeForDays = streakDays <= 0 ? 0 : (activeToday ? 1 : 0) + freezes,
            StreakFreezes = (byte)freezes,
            MaxStreakFreezes = _settings.EffectiveMaxStreakFreezes,
            StreakFreezesUsed = wallet?.StreakFreezesUsed ?? 0,
            DaysToNextMilestone = daysToMilestone,
            MilestoneSparks = _settings.EffectiveStreakMilestoneSparks,
            Message = RewardMessages.Headline(resolved, streakDays, activeToday, freezes, daysToMilestone),
            Items = items,
            ActiveBoosts = boosts
        };
    }

    public async Task<IReadOnlyList<ShopItemDto>> GetShopAsync(
        Guid userId, string? language = null, CancellationToken cancellationToken = default)
    {
        var resolved = NormalizeLanguage(language);

        var balance = await _db.LearnerWallets
            .AsNoTracking()
            .Where(w => w.UserId == userId)
            .Select(w => (int?)w.SparksBalance)
            .FirstOrDefaultAsync(cancellationToken) ?? 0;

        var owned = await _db.LearnerItems
            .AsNoTracking()
            .Where(i => i.UserId == userId)
            .ToDictionaryAsync(i => i.ShopItemId, i => i.Quantity, cancellationToken);

        var items = await _db.ShopItems
            .AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Id)
            .Select(s => new ShopItemDto
            {
                ItemId = s.Id,
                Code = s.Code,
                Kind = s.Kind,
                Name = resolved == English ? s.NameEn : s.NameAr,
                Description = resolved == English ? s.DescriptionEn : s.DescriptionAr,
                ImageUrl = s.ImageUrl,
                PriceSparks = s.PriceSparks,
                MaxOwned = s.MaxOwned,
                BoostMultiplier = s.BoostMultiplier,
                BoostMinutes = s.BoostMinutes
            })
            .ToListAsync(cancellationToken);

        foreach (var item in items)
        {
            item.Owned = owned.GetValueOrDefault(item.ItemId);
            item.AtMaxOwned = item.MaxOwned is byte cap && item.Owned >= cap;
            item.CanAfford = balance >= item.PriceSparks && !item.AtMaxOwned;
        }

        return items;
    }

    public async Task<PurchaseResultDto> PurchaseAsync(
        Guid userId, int itemId, string? language = null, CancellationToken cancellationToken = default)
    {
        var resolved = NormalizeLanguage(language);
        var now = _clock.UtcNow;

        var item = await _db.ShopItems
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == itemId, cancellationToken)
            ?? throw new KeyNotFoundException($"العنصر رقم {itemId} غير موجود");

        if (!item.IsActive)
            throw new BusinessRuleException($"العنصر '{Name(item, resolved)}' غير متاح للشراء حاليًا");

        var wallet = await _db.LearnerWallets.FirstOrDefaultAsync(w => w.UserId == userId, cancellationToken)
            ?? throw new BusinessRuleException("لا توجد شرارات كافية، أكمل درسًا أو اختبارًا لتجمع شرارات");

        if (wallet.SparksBalance < item.PriceSparks)
            throw new BusinessRuleException(
                $"تحتاج {item.PriceSparks} شرارة لشراء '{Name(item, resolved)}'، ولديك {wallet.SparksBalance} فقط");

        var learnerItem = await _db.LearnerItems
            .FirstOrDefaultAsync(i => i.UserId == userId && i.ShopItemId == itemId, cancellationToken);

        var owned = learnerItem?.Quantity ?? 0;

        // The holding cap is the whole reason a freeze does not let a child buy
        // their way out of the habit the streak is there to build.
        if (item.MaxOwned is byte cap && owned >= cap)
            throw new BusinessRuleException(
                $"لا يمكنك الاحتفاظ بأكثر من {cap} من '{Name(item, resolved)}' في نفس الوقت");

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        wallet.SparksBalance -= item.PriceSparks;
        wallet.UpdatedAt = now;

        _db.SparkTransactions.Add(new SparkTransaction
        {
            UserId = userId,
            Amount = -item.PriceSparks,
            Reason = SparkReasons.ShopPurchase,
            // Every purchase is its own event, so it carries no idempotency key:
            // buying two freezes is two purchases, not a replay of one.
            ReferenceKey = null,
            BalanceAfter = wallet.SparksBalance,
            CreatedAt = now
        });

        if (learnerItem is null)
        {
            learnerItem = new LearnerItem
            {
                UserId = userId,
                ShopItemId = itemId,
                Quantity = 1,
                AcquiredAt = now,
                UpdatedAt = now
            };

            _db.LearnerItems.Add(learnerItem);
        }
        else
        {
            learnerItem.Quantity += 1;
            learnerItem.UpdatedAt = now;
        }

        // A freeze is only useful as a number on the wallet — that is what the
        // streak rule reads when the child comes back after a missed day.
        if (item.Kind == ShopItemKinds.StreakFreeze)
            wallet.StreakFreezes = (byte)Math.Min(
                wallet.StreakFreezes + 1, _settings.EffectiveMaxStreakFreezes);

        ActiveBoostDto? activatedBoost = null;

        // A boost is a window in time, so buying it starts the clock: holding an
        // unused one in an inventory would only invite "why is my double XP gone?"
        if (item.Kind == ShopItemKinds.Boost
            && item.BoostMultiplier is byte multiplier
            && item.BoostMinutes is int minutes)
        {
            var expiresAt = now.AddMinutes(minutes);

            _db.LearnerBoosts.Add(new LearnerBoost
            {
                UserId = userId,
                ShopItemId = itemId,
                Multiplier = multiplier,
                StartedAt = now,
                ExpiresAt = expiresAt
            });

            activatedBoost = new ActiveBoostDto
            {
                ItemId = itemId,
                Code = item.Code,
                Name = Name(item, resolved),
                Multiplier = multiplier,
                ExpiresAt = expiresAt,
                SecondsRemaining = minutes * 60
            };
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Another request spent from the same wallet between our read and our
            // write. Nothing was saved, so the child's Sparks are intact and a
            // retry either succeeds or reports an honest "not enough".
            _db.ChangeTracker.Clear();

            throw new ConflictException(
                "تم تغيير رصيد الشرارات في نفس اللحظة، برجاء إعادة المحاولة", ex);
        }

        return new PurchaseResultDto
        {
            ItemId = itemId,
            Code = item.Code,
            Name = Name(item, resolved),
            PricePaid = item.PriceSparks,
            SparksBalance = wallet.SparksBalance,
            Owned = learnerItem.Quantity,
            ActivatedBoost = activatedBoost,
            Message = RewardMessages.Purchased(resolved, Name(item, resolved), item.PriceSparks)
        };
    }

    public async Task<LearnerGamificationDto> EquipAsync(
        Guid userId, int itemId, string? language = null, CancellationToken cancellationToken = default)
    {
        var resolved = NormalizeLanguage(language);

        var owned = await _db.LearnerItems
            .Include(i => i.ShopItem)
            .Where(i => i.UserId == userId)
            .ToListAsync(cancellationToken);

        var target = owned.FirstOrDefault(i => i.ShopItemId == itemId)
            ?? throw new KeyNotFoundException($"لا تمتلك العنصر رقم {itemId}");

        if (target.ShopItem.Kind != ShopItemKinds.Avatar)
            throw new BusinessRuleException("هذا العنصر ليس من عناصر المظهر");

        var now = _clock.UtcNow;

        // One equipped cosmetic at a time: taking the old one off is part of
        // putting the new one on, in the same save.
        foreach (var item in owned.Where(i => i.ShopItem.Kind == ShopItemKinds.Avatar && i.IsEquipped))
        {
            item.IsEquipped = false;
            item.UpdatedAt = now;
        }

        target.IsEquipped = true;
        target.UpdatedAt = now;

        await _db.SaveChangesAsync(cancellationToken);

        return await GetMineAsync(userId, resolved, cancellationToken);
    }

    private static string Name(ShopItem item, string language) =>
        language == English ? item.NameEn : item.NameAr;

    /// <summary>
    /// Same rule as the rest of the API: anything unrecognised falls back to
    /// Arabic rather than failing, and casing never decides anything.
    /// </summary>
    private static string NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return "ar";

        var code = language.Trim().ToLowerInvariant();
        var dash = code.IndexOf('-');

        if (dash > 0)
            code = code[..dash];

        return code == English ? English : "ar";
    }
}
