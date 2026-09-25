using System;
using System.Collections.Generic;

namespace UsersDA.Entities;

public partial class ParentChildLink
{
    public Guid Id { get; set; }

    public Guid ParentUserId { get; set; }

    public Guid ChildUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual User ChildUser { get; set; } = null!;

    public virtual User ParentUser { get; set; } = null!;
}
