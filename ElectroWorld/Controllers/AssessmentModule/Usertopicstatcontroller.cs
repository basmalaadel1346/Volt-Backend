using AssessmentBL.DTOs.UserTopicStat;
using AssessmentBL.Interfaces;
using ElectroWorld.Swagger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Common.Api;
using Shared.Users;

namespace ElectroWorld.Api.Controllers;

[ApiController]
[Route("api/user-topic-stats")]
[Authorize]
public class UserTopicStatController : ControllerBase
{
    private const string TopicExample = @"{
          ""topicId"": 4,
          ""name"": ""الثنائي الباعث للضوء"",
          ""description"": ""كيف يعمل الـ LED ولماذا له طرف طويل وطرف قصير."",
          ""categoryId"": 2,
          ""categoryName"": ""المكونات الإلكترونية"",
          ""learningLevel"": ""Intermediate"",
          ""mastery"": ""Practicing"",
          ""stars"": 2,
          ""xp"": 7,
          ""questionsAnswered"": 10,
          ""correctAnswers"": 7,
          ""wrongAnswers"": 3,
          ""accuracyPercentage"": 70,
          ""hintsUsed"": 2,
          ""lastPracticedAt"": ""2026-09-10T18:42:10.123"",
          ""message"": ""أنت تتقدّم بسرعة! راجع الأسئلة التي أخطأت فيها لتتقن هذا الموضوع."",
          ""difficulties"": [
            { ""difficulty"": ""Easy"", ""xp"": 6, ""questionsAnswered"": 8, ""correctAnswers"": 6, ""wrongAnswers"": 2, ""accuracyPercentage"": 75, ""hintsUsed"": 2 },
            { ""difficulty"": ""Medium"", ""xp"": 1, ""questionsAnswered"": 2, ""correctAnswers"": 1, ""wrongAnswers"": 1, ""accuracyPercentage"": 50, ""hintsUsed"": 0 }
          ]
        }";

    private readonly IUserTopicStatService _userTopicStatService;

    public UserTopicStatController(IUserTopicStatService userTopicStatService)
    {
        _userTopicStatService = userTopicStatService;
    }

    /// <summary>Your progress map: XP, stars and mastery for every topic, grouped by category.</summary>
    /// <remarks>
    /// Every active topic of an active category is listed, practised or not
    /// (NotStarted, 0 stars), so the app can show what is still ahead; a retired
    /// topic stays listed for a child who practised it. Names and messages follow ?language= →
    /// Accept-Language → ar.
    ///
    /// totalXp is every point earned in submitted attempts — correct
    /// MultipleChoice/TrueFalse answers and graded essays — including questions
    /// that belong to no topic, so it can exceed the sum of the topics' xp.
    /// Mastery and accuracy count MultipleChoice/TrueFalse answers only; a hint
    /// counts in hintsUsed only if the child was actually shown it.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(typeof(MyProgressResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [SwaggerExample(200, @"{
      ""language"": ""ar"",
      ""totalXp"": 23,
      ""totalTopics"": 6,
      ""topicsStarted"": 3,
      ""topicsMastered"": 1,
      ""questionsAnswered"": 26,
      ""correctAnswers"": 21,
      ""accuracyPercentage"": 81,
      ""hintsUsed"": 2,
      ""message"": ""أحسنت! جمعت 23 من نقاط الخبرة وأتقنت 1 من أصل 6 من المواضيع. واصل التقدّم!"",
      ""categories"": [
        {
          ""categoryId"": 2,
          ""name"": ""المكونات الإلكترونية"",
          ""xp"": 7,
          ""totalTopics"": 3,
          ""topicsStarted"": 1,
          ""topicsMastered"": 0,
          ""topics"": [
            " + TopicExample + @",
            {
              ""topicId"": 5,
              ""name"": ""المقاومة"",
              ""description"": ""لماذا نحتاج المقاومة في الدائرة وكيف نقرأ قيمتها."",
              ""categoryId"": 2,
              ""categoryName"": ""المكونات الإلكترونية"",
              ""learningLevel"": ""Intermediate"",
              ""mastery"": ""NotStarted"",
              ""stars"": 0,
              ""xp"": 0,
              ""questionsAnswered"": 0,
              ""correctAnswers"": 0,
              ""wrongAnswers"": 0,
              ""accuracyPercentage"": 0,
              ""hintsUsed"": 0,
              ""lastPracticedAt"": null,
              ""message"": ""موضوع جديد في انتظارك! ابدأ أول اختبار فيه لتجمع نقاط الخبرة."",
              ""difficulties"": []
            }
          ]
        }
      ]
    }")]
    [SwaggerExample(401, ApiResponseExamples.Unauthorized)]
    public async Task<ActionResult<MyProgressResponseDto>> GetMine(
        [FromQuery] string? language,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        var progress = await _userTopicStatService.GetMyProgressAsync(
            userId, Request.ResolveContentLanguage(language), ct);
        return Ok(progress);
    }

    /// <summary>Your progress in one topic, with a breakdown per difficulty.</summary>
    /// <remarks>
    /// A topic you have not practised yet is a 200 with mastery "NotStarted"; 404
    /// only when the topic does not exist.
    /// </remarks>
    [HttpGet("{topicId:int}")]
    [ProducesResponseType(typeof(TopicProgressDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [SwaggerExample(200, TopicExample)]
    [SwaggerExample(401, ApiResponseExamples.Unauthorized)]
    [SwaggerExample(404, @"{""success"":false,""message"":""الموضوع رقم 4 غير موجود"",""data"":null}")]
    public async Task<ActionResult<TopicProgressDto>> GetTopic(
        int topicId,
        [FromQuery] string? language,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        var progress = await _userTopicStatService.GetTopicProgressAsync(
            userId, topicId, Request.ResolveContentLanguage(language), ct);
        return Ok(progress);
    }

    // No recalculate endpoint by design. UpdateAfterQuizAttemptAsync accumulates
    // (+=) into the stats row and is not idempotent, so exposing it would let any
    // user inflate their own counters without bound by re-posting it. It stays an
    // internal step of SubmitAsync, inside that method's transaction.
}
