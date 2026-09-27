using ElectroWorld.Api;
using ElectroWorld.Api.Controllers;
using ElectroWorld.Swagger;
using GamificationBL.DTOs;
using GamificationBL.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Shared.Common.Api;
using Shared.Users;

namespace ElectroWorld.Controllers.Gamification;

/// <summary>
/// Sparks, streaks and the shop — the loop that gets a child to open the app
/// tomorrow.
///
/// Nothing here GRANTS anything: Sparks and streak days are earned by finishing
/// lessons and quizzes, automatically, and arrive in those responses' `rewards`.
/// This controller is where the child SEES what they have and SPENDS it.
/// </summary>
[ApiController]
[Route("api/gamification")]
[Authorize]
public class GamificationController : ControllerBase
{
    private readonly IGamificationService _gamification;

    public GamificationController(IGamificationService gamification)
    {
        _gamification = gamification;
    }

    /// <summary>Your Sparks, your streak, what you own and any boost running now.</summary>
    /// <remarks>
    /// Everything the reward bar draws, in one call.
    ///
    /// `streakSafeForDays` is the number the child actually cares about: how long
    /// the streak survives without doing anything, freezes included. 0 means it
    /// breaks unless they learn something today.
    ///
    /// A learner who has earned nothing yet gets zeros, not a 404.
    /// </remarks>
    [HttpGet("me")]
    [OutputCache(PolicyName = ResponseCachingPolicies.PerLearner)]
    [ProducesResponseType(typeof(LearnerGamificationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [SwaggerExample(200, @"{
      ""language"": ""ar"",
      ""sparksBalance"": 145,
      ""lifetimeSparks"": 320,
      ""currentStreakDays"": 4,
      ""longestStreakDays"": 9,
      ""lastActivityOn"": ""2026-09-21"",
      ""activeToday"": true,
      ""streakSafeForDays"": 2,
      ""streakFreezes"": 1,
      ""maxStreakFreezes"": 2,
      ""streakFreezesUsed"": 3,
      ""daysToNextMilestone"": 3,
      ""milestoneSparks"": 50,
      ""message"": ""‏4 أيام متتالية، واصل! باقي 3 أيام على صندوق المكافأة التالي."",
      ""items"": [
        { ""itemId"": 1, ""code"": ""streak_freeze"", ""kind"": ""StreakFreeze"", ""name"": ""تجميد السلسلة"", ""quantity"": 1, ""isEquipped"": false }
      ],
      ""activeBoosts"": []
    }")]
    [SwaggerExample(401, ApiResponseExamples.Unauthorized)]
    public async Task<ActionResult<LearnerGamificationDto>> GetMine(
        [FromQuery] string? language, CancellationToken ct)
    {
        var mine = await _gamification.GetMineAsync(
            User.GetUserId(), Request.ResolveContentLanguage(language), ct);
        return Ok(mine);
    }

    /// <summary>The shop, priced for you.</summary>
    /// <remarks>
    /// Each item says what it costs, how many you already own, the cap on holding
    /// it, and `canAfford` — so the app can grey out a tile without doing the
    /// arithmetic itself.
    ///
    /// The important item is the streak freeze: an extra life the system spends
    /// automatically when the child misses a day. It is capped (two by default)
    /// on purpose — buying ten would turn the streak into something that no
    /// longer measures a habit.
    /// </remarks>
    [HttpGet("shop")]
    [OutputCache(PolicyName = ResponseCachingPolicies.PerLearner)]
    [ProducesResponseType(typeof(IReadOnlyList<ShopItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [SwaggerExample(200, @"[
      {
        ""itemId"": 1,
        ""code"": ""streak_freeze"",
        ""kind"": ""StreakFreeze"",
        ""name"": ""تجميد السلسلة"",
        ""description"": ""بيحمي سلسلتك لو نسيت تدخل يوم."",
        ""priceSparks"": 200,
        ""owned"": 1,
        ""maxOwned"": 2,
        ""canAfford"": false,
        ""atMaxOwned"": false
      },
      {
        ""itemId"": 2,
        ""code"": ""double_xp_15"",
        ""kind"": ""Boost"",
        ""name"": ""نقاط خبرة مضاعفة"",
        ""priceSparks"": 120,
        ""owned"": 0,
        ""boostMultiplier"": 2,
        ""boostMinutes"": 15,
        ""canAfford"": true,
        ""atMaxOwned"": false
      }
    ]")]
    [SwaggerExample(401, ApiResponseExamples.Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<ShopItemDto>>> GetShop(
        [FromQuery] string? language, CancellationToken ct)
    {
        var shop = await _gamification.GetShopAsync(
            User.GetUserId(), Request.ResolveContentLanguage(language), ct);
        return Ok(shop);
    }

    /// <summary>Buys one item with Sparks.</summary>
    /// <remarks>
    /// Buying a streak freeze puts it in your inventory, where the system spends
    /// it automatically the next time you come back after a missed day — you never
    /// "use" a freeze yourself.
    ///
    /// Buying a boost STARTS it: a timed multiplier held unused in an inventory
    /// would only invite "why is my double XP gone?".
    ///
    /// 400 when you cannot afford it or already hold the maximum.
    /// 409 when a concurrent purchase spent the same Sparks first — nothing was
    /// charged, so a retry either succeeds or reports an honest "not enough".
    /// </remarks>
    [HttpPost("shop/{itemId:int}/purchase")]
    [ProducesResponseType(typeof(PurchaseResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    [SwaggerExample(200, @"{
      ""itemId"": 1,
      ""code"": ""streak_freeze"",
      ""name"": ""تجميد السلسلة"",
      ""pricePaid"": 200,
      ""sparksBalance"": 45,
      ""owned"": 2,
      ""message"": ""اشتريت تجميد السلسلة مقابل 200 شرارة.""
    }")]
    [SwaggerExample(400, @"{""success"":false,""message"":""تحتاج 200 شرارة لشراء 'تجميد السلسلة'، ولديك 45 فقط"",""data"":null}")]
    [SwaggerExample(401, ApiResponseExamples.Unauthorized)]
    public async Task<ActionResult<PurchaseResultDto>> Purchase(
        int itemId, [FromQuery] string? language, CancellationToken ct)
    {
        var result = await _gamification.PurchaseAsync(
            User.GetUserId(), itemId, Request.ResolveContentLanguage(language), ct);
        return Ok(result);
    }

    /// <summary>Wears an avatar item you own (and takes off the previous one).</summary>
    /// <remarks>
    /// Cosmetics only — one worn at a time. Returns your whole gamification state,
    /// so the app can redraw without a second call.
    /// </remarks>
    [HttpPost("items/{itemId:int}/equip")]
    [ProducesResponseType(typeof(LearnerGamificationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [SwaggerExample(400, @"{""success"":false,""message"":""هذا العنصر ليس من عناصر المظهر"",""data"":null}")]
    [SwaggerExample(404, @"{""success"":false,""message"":""لا تمتلك العنصر رقم 4"",""data"":null}")]
    public async Task<ActionResult<LearnerGamificationDto>> Equip(
        int itemId, [FromQuery] string? language, CancellationToken ct)
    {
        var mine = await _gamification.EquipAsync(
            User.GetUserId(), itemId, Request.ResolveContentLanguage(language), ct);
        return Ok(mine);
    }
}
