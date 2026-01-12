using Microsoft.AspNetCore.Identity;

namespace PbSqlServerMonitoring.Models;

public class ApplicationUser : IdentityUser
{
    // Add custom properties here if needed
    public string? FullName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
