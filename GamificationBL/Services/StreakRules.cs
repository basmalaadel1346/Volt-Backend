namespace GamificationBL.Services;

/// <summary>
/// The streak rule, as pure arithmetic over dates. Kept away from the database
/// so it can be reasoned about and tested directly: everything about a streak
/// that is easy to get subtly wrong — the day boundary, what "missed" means,
/// how many freezes a gap costs — is decided here and nowhere else.
///
/// A streak counts DAYS, not sessions: several activities on one day advance it
/// once, which is what stops a child farming it in a single afternoon.
/// </summary>
public static class StreakRules
{
    /// <summary>
    /// Applies one day's activity to a learner's streak.
    /// </summary>
    /// <param name="lastActivityOn">The day of the last counted activity, or null for a first-ever one.</param>
    /// <param name="today">The day of the activity being counted.</param>
    /// <param name="currentStreak">Consecutive days before this activity.</param>
    /// <param name="freezesAvailable">Freezes the learner holds.</param>
    public static StreakChange Apply(
        DateOnly? lastActivityOn,
        DateOnly today,
        int currentStreak,
        byte freezesAvailable)
    {
        // First activity ever, or the first after a wipe.
        if (lastActivityOn is not DateOnly last)
            return new StreakChange(1, 0, Extended: true, Broken: false);

        // Already counted today. Doing more today is good, but it is still one day.
        if (last >= today)
            return new StreakChange(Math.Max(currentStreak, 1), 0, Extended: false, Broken: false);

        var gapDays = today.DayNumber - last.DayNumber;

        // Yesterday: the ordinary case.
        if (gapDays == 1)
            return new StreakChange(currentStreak + 1, 0, Extended: true, Broken: false);

        // Days the child was away. Coming back on Thursday after Monday means
        // Tuesday and Wednesday were missed — two days, two freezes.
        var missedDays = gapDays - 1;

        if (missedDays <= freezesAvailable)
            return new StreakChange(
                currentStreak + 1, (byte)missedDays, Extended: true, Broken: false);

        // Not enough to cover the gap. The freezes are NOT spent: taking them for
        // a rescue that did not happen would be the one thing more discouraging
        // than losing the streak.
        return new StreakChange(1, 0, Extended: true, Broken: true);
    }

    /// <summary>
    /// True when a streak of this length has just crossed a milestone worth a
    /// reward — every <paramref name="everyDays"/> days.
    /// </summary>
    public static bool IsMilestone(int streakDays, int everyDays) =>
        everyDays > 0 && streakDays > 0 && streakDays % everyDays == 0;
}

/// <param name="StreakDays">The streak after this activity.</param>
/// <param name="FreezesSpent">Freezes consumed to bridge missed days.</param>
/// <param name="Extended">The streak grew (or started) because of this activity.</param>
/// <param name="Broken">The gap was too long to bridge, so the streak restarted at 1.</param>
public sealed record StreakChange(int StreakDays, byte FreezesSpent, bool Extended, bool Broken);
