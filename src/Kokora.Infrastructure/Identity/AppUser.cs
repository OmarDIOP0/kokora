using Microsoft.AspNetCore.Identity;

namespace Kokora.Infrastructure.Identity;

public class AppUser : IdentityUser
{
    public string DisplayName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSeenAt { get; set; }
}
