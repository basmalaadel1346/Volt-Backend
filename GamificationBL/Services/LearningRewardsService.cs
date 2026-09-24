using GamificationBL.Services.Constants;
using GamificationDA.Context;
using GamificationDA.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Common.Abstractions;
using Shared.Gamification;

namespace GamificationBL.Services;

/// <summary>
/// Turns "the child just finished something" into Sparks and a longer streak.
///
/// Two properties matter more than the arithmetic:
///
///   IDEMPOTENT — a replayed submit, a retried request or two instances racing
///   must pay once. Every earning writes a SparkTransaction whose
///   (UserId, Reason, ReferenceKey) is unique in the database, so the duplicate
///   loses at the index rather than at a check that could be raced past.
///
///   HARMLESS ON FAILURE — the caller has already committed a lesson or a quiz
///   result. Nothing here may turn that into an error the child sees, so the
///   public entry point never throws; it logs and reports that nothing was
///   granted.
/// </summary>
public sealed class LearningRewardsService : ILearningRewards
{
    /// <summary>Concurrency retries before giving up on a reward. Two racers, one retry each.</summary>
    private const int MaxConcurrencyRetries = 3;

    private readonly GamificationDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly GamificationSettings _settings;
    private readonly ILogger<LearningRewardsService> _logger;

    public LearningRewardsService(
        GamificationDbContext db,
        IDateTimeProvider clock,
        IOptions<GamificationSettings> settings,
        ILogger<LearningRewardsService> logger)
    {
        _db = db;
        _clock = clock;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<RewardOutcome> RecordActivityAsync(
        LearningActivity activity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activity);

        for (var attempt = 1; attempt <= MaxConcurrencyRetries; attempt++)
        {
            try
            {
                return await GrantAsync(activity, cancellationToken);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxConcurrencyRetries)
            {
                // Another activity of the same child updated the wallet between our
                // read and our write. Re-read and apply on top — the streak and the
                // balance are both running totals, so this always converges.
                _db.ChangeTracker.Clear();
            }
            catch (DbUpdateException ex) when (IsDuplicateReward(ex))
            {
                // This exact activity has already been paid for. Not an error: it
                // is the guarantee working.
                _db.ChangeTracker.Clear();
                return await DescribeWalletAsync(activity.UserId, cancellationToken);
            }
            catch (Exception ex)
            {
                _db.ChangeTracker.Clear();
                _logger.LogError(ex,
                    "Rewards for {Kind} {ReferenceKey} of user {UserId} could not be granted.",
                    activity.Kind, activity.ReferenceKey, activity.UserId);

                return RewardOutcome.None;
            }
        }

        _logger.LogWarning(
            "Rewards for {Kind} {ReferenceKey} of user {UserId} were abandoned after {Retries} concurrency conflicts.",
            activity.Kind, activity.ReferenceKey, activity.UserId, MaxConcurrencyRetries);

        return await DescribeWalletAsync(activity.UserId, cancellationToken);
    }

    private async Task<RewardOutcome> GrantAsync(LearningActivity activity, CancellationToken cancellationToken)
    {
        // Checked BEFORE the wallet is touched, so a replayed activity does not
        // advance the streak either: a second submit of the same attempt is not a
        // second day of learning.
        if (await AlreadyPaidAsync(activity, cancellationToken))
            return await DescribeWalletAsync(activity.UserId, cancellationToken);

        var wallet = await LoadOrCreateWalletAsync(activity.UserId, cancellationToken);
        var now = _clock.UtcNow;
        var today = DateOnly.FromDateTime(now);

        var streak = StreakRules.Apply(
            wallet.LastActivityOn, today, wallet.CurrentStreakDays, wallet.StreakFreezes);

        var lines = new List<RewardLine>();
        var earned = 0;

        foreach (var (reason, sparks) in EarningsFor(activity))
        {
            if (sparks <= 0)
                continue;

            earned += sparks;
            AddTransaction(wallet, sparks, reason, activity.ReferenceKey, now);
            lines.Add(new RewardLine(reason, sparks, RewardMessages.Line(reason, sparks)));
        }

        // The streak reward box. Keyed on the ACTIVITY, like every other line —
        // not on the streak length, which looks tempting but is wrong: a child
        // who reaches day 7, breaks the streak and later reaches day 7 again
        // would hit the unique index on the second occasion and lose the whole
        // activity's reward with it. Paying once per milestone is already
        // guaranteed by the Extended check: only the activity that advances the
        // streak onto day 7 can reach this branch, so a second quiz the same day
        // never does.
        if (streak.Extended
            && StreakRules.IsMilestone(streak.StreakDays, _settings.EffectiveStreakMilestoneDays))
        {
            var milestoneSparks = _settings.EffectiveStreakMilestoneSparks;

            if (milestoneSparks > 0)
            {
                earned += milestoneSparks;
                AddTransaction(
                    wallet, milestoneSparks, SparkReasons.StreakMilestone, activity.ReferenceKey, now);

                lines.Add(new RewardLine(
                    SparkReasons.StreakMilestone,
                    milestoneSparks,
                    RewardMessages.Milestone(streak.StreakDays, milestoneSparks)));
            }
        }

        wallet.CurrentStreakDays = streak.StreakDays;
        wallet.LongestStreakDays = Math.Max(wallet.LongestStreakDays, streak.StreakDays);
        wallet.LastActivityOn = today;
        wallet.StreakFreezes = (byte)(wallet.StreakFreezes - streak.FreezesSpent);
        wallet.StreakFreezesUsed += streak.FreezesSpent;
        wallet.UpdatedAt = now;

        await _db.SaveChangesAsync(cancellationToken);

        return new RewardOutcome(
            earned,
            wallet.SparksBalance,
            wallet.CurrentStreakDays,
            wallet.LongestStreakDays,
            streak.Extended,
            streak.FreezesSpent,
            wallet.StreakFreezes,
            lines);
    }

    /// <summary>What an activity is worth, as the lines it produces.</summary>
    private IEnumerable<(string Reason, int Sparks)> EarningsFor(LearningActivity activity)
    {
        switch (activity.Kind)
        {
            case LearningActivityKind.LessonCompleted:
                yield return (SparkReasons.LessonCompleted, _settings.EffectiveLessonCompletedSparks);
                break;

            case LearningActivityKind.PlacementCompleted:
                yield return (SparkReasons.PlacementCompleted, _settings.EffectivePlacementCompletedSparks);
                break;

            case LearningActivityKind.LevelSkipPassed:
                yield return (SparkReasons.LevelSkipPassed, _settings.EffectiveLevelSkipPassedSparks);
                break;

            default:
                yield return (SparkReasons.QuizCompleted, _settings.EffectiveQuizCompletedSparks);
                break;
        }

        // The perfect-score bonus rides on any quiz, including a level-skip run.
        if (activity.PerfectScore && activity.Kind != LearningActivityKind.LessonCompleted)
            yield return (SparkReasons.PerfectScore, _settings.EffectivePerfectScoreBonusSparks);
    }

    private void AddTransaction(
        LearnerWallet wallet, int amount, string reason, string? referenceKey, DateTime now)
    {
        wallet.SparksBalance += amount;
        wallet.LifetimeSparks += amount;

        // Added through the navigation, not through the DbSet: on a brand-new
        // wallet that is what tells EF to insert the wallet before the row that
        // references it.
        wallet.SparkTransactions.Add(new SparkTransaction
        {
            UserId = wallet.UserId,
            Amount = amount,
            Reason = reason,
            ReferenceKey = referenceKey,
            BalanceAfter = wallet.SparksBalance,
            CreatedAt = now
        });
    }

    /// <summary>
    /// Has any line of this activity already been written? One is enough: the
    /// lines of an activity are always written together in one transaction.
    /// </summary>
    private Task<bool> AlreadyPaidAsync(LearningActivity activity, CancellationToken cancellationToken) =>
        _db.SparkTransactions
            .AsNoTracking()
            .AnyAsync(
                t => t.UserId == activity.UserId && t.ReferenceKey == activity.ReferenceKey,
                cancellationToken);

    private async Task<LearnerWallet> LoadOrCreateWalletAsync(Guid userId, CancellationToken cancellationToken)
    {
        var wallet = await _db.LearnerWallets.FirstOrDefaultAsync(w => w.UserId == userId, cancellationToken);

        if (wallet is not null)
            return wallet;

        var now = _clock.UtcNow;

        wallet = new LearnerWallet
        {
            UserId = userId,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.LearnerWallets.Add(wallet);
        return wallet;
    }

    /// <summary>The wallet as it stands, granting nothing. What a duplicate activity gets back.</summary>
    private async Task<RewardOutcome> DescribeWalletAsync(Guid userId, CancellationToken cancellationToken)
    {
        var wallet = await _db.LearnerWallets
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.UserId == userId, cancellationToken);

        return wallet is null
            ? RewardOutcome.None
            : new RewardOutcome(
                0,
                wallet.SparksBalance,
                wallet.CurrentStreakDays,
                wallet.LongestStreakDays,
                StreakExtendedToday: wallet.LastActivityOn == DateOnly.FromDateTime(_clock.UtcNow),
                FreezesSpent: 0,
                FreezesAvailable: wallet.StreakFreezes,
                Lines: []);
    }

    private static bool IsDuplicateReward(DbUpdateException exception) =>
        exception.InnerException?.Message
            .Contains("UQ_SparkTransactions_UserId_Reason_ReferenceKey", StringComparison.OrdinalIgnoreCase) == true;
}
