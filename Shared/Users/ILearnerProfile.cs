namespace Shared.Users;

/// <summary>
/// The little a module may learn about a learner without referencing the Users
/// module. Today: their age, used only to pitch an AI hint at the right level
/// (docs/AI_CONTRACT.md). Implemented in UsersBL.
///
/// Deliberately not a profile: no name, no email, no identifiers. If another
/// field is ever needed here, check it against the never-send list first.
/// </summary>
public interface ILearnerProfile
{
    /// <summary>
    /// The learner's age, or null when unknown — the field is optional in the
    /// Users schema, and guest accounts rarely have it.
    /// </summary>
    Task<int?> GetAgeAsync(Guid userId, CancellationToken cancellationToken = default);
}
