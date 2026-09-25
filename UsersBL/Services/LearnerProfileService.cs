using Shared.Users;
using UsersDA.Interfaces;

namespace UsersBL.Services;

// [Assessment-AI] Implements the Shared contract ILearnerProfile so Assessment can
// pitch an AI hint at the child's age (addendum v1.1 §D.3 learnerContext.age).
// Read-only over the existing IUserRepository — no new table, column, entity or
// endpoint in Users. Age is the ONLY field exposed: never name, email or provider.
// If this is ever removed, AI hints simply stop carrying the age.
public class LearnerProfileService : ILearnerProfile
{
    private readonly IUserRepository _userRepository;

    public LearnerProfileService(IUserRepository userRepository) => _userRepository = userRepository;

    public async Task<int?> GetAgeAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        return user?.Age;
    }
}
