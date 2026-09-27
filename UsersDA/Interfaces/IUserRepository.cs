using UsersDA.Entities;

namespace UsersDA.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<User?> GetByProviderAsync(string provider, string providerUserId, CancellationToken ct = default);
    Task<bool> EmailExistsAsync(string email, CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
    void Remove(User user);

    /// <summary>بتمسح أي صفوف ParentChildLinks اليوزر ده طرف فيها (كـ Parent أو كـ Child) -
    /// لازم تتعمل قبل مسح اليوزر نفسه عشان الـ FK بتاع ChildUserId مالوش Cascade.</summary>
    Task RemoveParentChildLinksForUserAsync(Guid userId, CancellationToken ct = default);
}
