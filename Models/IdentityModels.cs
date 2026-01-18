using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace PbSqlServerMonitoring.Models;

public class ApplicationUser : IdentityUser
{
    /// <summary>
    /// User's display name.
    /// </summary>
    public string? FullName { get; set; }

    /// <summary>
    /// When the user account was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Per-user maximum connections limit.
    /// If null, uses the global default from appsettings (Monitoring:DefaultUserMaxConnections).
    /// </summary>
    public int? MaxConnections { get; set; }

    /// <summary>
    /// Whether this user account is active.
    /// Inactive users cannot login or use the API.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// When the user last logged in.
    /// </summary>
    public DateTime? LastLoginAt { get; set; }
}

[Table("UserPreferences")]
public class UserPreferenceEntity
{
    [Key]
    [MaxLength(64)]
    public string UserIdentifier { get; set; } = "";

    /// <summary>
    /// JSON serialized user preferences
    /// </summary>
    public string PreferencesJson { get; set; } = "{}";

    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
