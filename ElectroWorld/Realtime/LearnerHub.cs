using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Shared.Users;

namespace ElectroWorld.Realtime;

/// <summary>
/// The learner's live connection, at /hubs/learner.
///
/// It exists so the app does not have to ask "are you done yet?" every couple of
/// minutes. Work that finishes after a request has returned — the AI writing
/// hints, the AI grading essays — announces itself here, and the screen updates
/// the moment it lands.
///
/// The hub sends; it never receives. Everything a client could ask for has an
/// ordinary endpoint, and keeping the hub one-way means a dropped connection can
/// only ever cost a notification, never a result. Each message names the
/// endpoint that returns the same information, so a client that missed the push
/// (backgrounded, offline, reconnecting) is never stuck waiting for a second one.
///
/// Every connection is authenticated, and messages go to the user group named by
/// the token's sub claim — never broadcast.
/// </summary>
[Authorize]
public sealed class LearnerHub : Hub
{
    /// <summary>The group one learner's connections share. Their own id: nothing else may join it.</summary>
    public static string GroupFor(Guid userId) => $"learner:{userId}";

    public override async Task OnConnectedAsync()
    {
        // From the token, never from the client: a connection cannot ask to listen
        // to somebody else's results.
        var userId = Context.User?.GetUserId() ?? Guid.Empty;

        if (userId != Guid.Empty)
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(userId));

        await base.OnConnectedAsync();
    }
}

/// <summary>The message names the client subscribes to. One place, so they cannot drift.</summary>
public static class LearnerHubEvents
{
    /// <summary>The AI finished the hints for an attempt: GET .../retry-questions.</summary>
    public const string HintsReady = "hintsReady";

    /// <summary>The AI graded some essays of an attempt: GET .../result.</summary>
    public const string EssaysGraded = "essaysGraded";

    /// <summary>Sparks, streak or inventory changed: GET /api/gamification/me.</summary>
    public const string RewardsChanged = "rewardsChanged";
}
