namespace Shared.Common.Text;

/// <summary>
/// The whole API's rule for text that stands for an enum — quizType,
/// questionType, difficulty, learningLevel, status. The values live as strings
/// (they are CHECK-constrained columns, not CLR enums), so a client sending
/// "multiplechoice" used to be rejected while the one field that happened to
/// normalize (learningLevel) accepted it. Every validator now goes through here,
/// so casing and surrounding whitespace never decide whether a request works.
///
/// Matching is case-insensitive; the CANONICAL spelling is what comes back, so
/// what reaches the database is always the exact value the CHECK constraint
/// allows.
/// </summary>
public static class CanonicalValues
{
    /// <summary>
    /// The canonical spelling of <paramref name="value"/>, or null when it is
    /// blank or not one of <paramref name="allowed"/>.
    /// </summary>
    public static string? Match(IEnumerable<string> allowed, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();

        return allowed.FirstOrDefault(a => string.Equals(a, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>True when <paramref name="value"/> is one of <paramref name="allowed"/>, whatever its casing.</summary>
    public static bool Contains(IEnumerable<string> allowed, string? value) => Match(allowed, value) is not null;

    /// <summary>The allowed values as one " | "-separated list, for an error message.</summary>
    public static string Describe(IEnumerable<string> allowed) => string.Join(" | ", allowed);
}
