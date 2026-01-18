using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PbSqlServerMonitoring.Models;
using PbSqlServerMonitoring.Services;

namespace PbSqlServerMonitoring.Controllers;

/// <summary>
/// Admin-only controller for managing users and viewing all connections.
///
/// Features:
/// - List all users with their connection counts
/// - Update user settings (maxConnections, isActive)
/// - Assign/remove admin role
/// - View all connections across all users
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/[controller]")]
public sealed class AdminController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IMultiConnectionService _connectionService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IMultiConnectionService connectionService,
        IConfiguration configuration,
        ILogger<AdminController> logger)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _connectionService = connectionService;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Gets all users with their roles and connection counts.
    /// </summary>
    [HttpGet("users")]
    [ProducesResponseType(typeof(List<UserInfoResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllUsers()
    {
        var users = await _userManager.Users.ToListAsync();
        var result = new List<UserInfoResponse>();

        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            var connectionCount = _connectionService.GetStatus().Connections
                .Count(c => _connectionService.GetConnection(c.Id)?.UserId == user.Id);

            result.Add(new UserInfoResponse
            {
                Id = user.Id,
                Email = user.Email ?? string.Empty,
                FullName = user.FullName,
                IsActive = user.IsActive,
                MaxConnections = user.MaxConnections,
                EffectiveMaxConnections = GetEffectiveMaxConnections(user),
                Roles = roles.ToList(),
                ConnectionCount = connectionCount,
                CreatedAt = user.CreatedAt,
                LastLoginAt = user.LastLoginAt,
                EmailConfirmed = user.EmailConfirmed
            });
        }

        return Ok(result);
    }

    /// <summary>
    /// Gets a specific user by ID.
    /// </summary>
    [HttpGet("users/{id}")]
    [ProducesResponseType(typeof(UserInfoResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUser(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
        {
            return NotFound(new { success = false, message = "User not found." });
        }

        var roles = await _userManager.GetRolesAsync(user);
        var connectionCount = _connectionService.GetStatus().Connections
            .Count(c => _connectionService.GetConnection(c.Id)?.UserId == user.Id);

        return Ok(new UserInfoResponse
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            FullName = user.FullName,
            IsActive = user.IsActive,
            MaxConnections = user.MaxConnections,
            EffectiveMaxConnections = GetEffectiveMaxConnections(user),
            Roles = roles.ToList(),
            ConnectionCount = connectionCount,
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt,
            EmailConfirmed = user.EmailConfirmed
        });
    }

    /// <summary>
    /// Updates a user's settings.
    /// </summary>
    [HttpPut("users/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateUser(string id, [FromBody] UpdateUserRequest request)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
        {
            return NotFound(new { success = false, message = "User not found." });
        }

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (user.Id == currentUserId && request.IsActive == false)
        {
            return BadRequest(new { success = false, message = "Cannot deactivate your own account." });
        }

        if (request.FullName != null)
            user.FullName = request.FullName;

        if (request.MaxConnections.HasValue)
            user.MaxConnections = request.MaxConnections > 0 ? request.MaxConnections : null;

        if (request.IsActive.HasValue)
            user.IsActive = request.IsActive.Value;

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return BadRequest(new { success = false, message = string.Join(", ", result.Errors.Select(e => e.Description)) });
        }

        _logger.LogInformation("Admin updated user {UserId}: MaxConnections={Max}, IsActive={Active}",
            id, user.MaxConnections, user.IsActive);

        return Ok(new { success = true, message = "User updated successfully." });
    }

    /// <summary>
    /// Deletes a user and all their connections.
    /// </summary>
    [HttpDelete("users/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteUser(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
        {
            return NotFound(new { success = false, message = "User not found." });
        }

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (user.Id == currentUserId)
        {
            return BadRequest(new { success = false, message = "Cannot delete your own account." });
        }

        var userConnections = _connectionService.GetStatus().Connections
            .Where(c => _connectionService.GetConnection(c.Id)?.UserId == id)
            .ToList();

        foreach (var conn in userConnections)
        {
            await _connectionService.RemoveConnectionAsync(conn.Id);
        }

        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            return BadRequest(new { success = false, message = string.Join(", ", result.Errors.Select(e => e.Description)) });
        }

        _logger.LogInformation("Admin deleted user {UserId} ({Email}) and {Count} connections",
            id, user.Email, userConnections.Count);

        return Ok(new { success = true, message = $"User and {userConnections.Count} connections deleted." });
    }

    /// <summary>
    /// Assigns or removes the Admin role from a user.
    /// </summary>
    [HttpPost("users/{id}/role")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetAdminRole(string id, [FromBody] SetRoleRequest request)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
        {
            return NotFound(new { success = false, message = "User not found." });
        }

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (user.Id == currentUserId && !request.IsAdmin)
        {
            return BadRequest(new { success = false, message = "Cannot remove your own admin role." });
        }

        var isCurrentlyAdmin = await _userManager.IsInRoleAsync(user, "Admin");

        if (request.IsAdmin && !isCurrentlyAdmin)
        {
            await _userManager.AddToRoleAsync(user, "Admin");
            _logger.LogInformation("Admin promoted user {UserId} ({Email}) to Admin", id, user.Email);
            return Ok(new { success = true, message = "User promoted to Admin." });
        }
        else if (!request.IsAdmin && isCurrentlyAdmin)
        {
            await _userManager.RemoveFromRoleAsync(user, "Admin");
            _logger.LogInformation("Admin demoted user {UserId} ({Email}) from Admin", id, user.Email);
            return Ok(new { success = true, message = "Admin role removed from user." });
        }

        return Ok(new { success = true, message = "No role change needed." });
    }

    /// <summary>
    /// Gets ALL connections from ALL users (admin overview).
    /// </summary>
    [HttpGet("connections")]
    [ProducesResponseType(typeof(AdminConnectionsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllConnections()
    {
        var status = _connectionService.GetStatus();
        var users = await _userManager.Users.ToDictionaryAsync(u => u.Id, u => u.Email ?? u.Id);

        var connectionsWithOwners = status.Connections.Select(c =>
        {
            var conn = _connectionService.GetConnection(c.Id);
            return new AdminConnectionInfo
            {
                Id = c.Id,
                Name = c.Name,
                Server = c.Server,
                Database = c.Database,
                Status = c.Status,
                IsEnabled = c.IsEnabled,
                CreatedAt = c.CreatedAt,
                LastSuccessfulConnection = c.LastSuccessfulConnection,
                LastError = c.LastError,
                UserId = conn?.UserId ?? string.Empty,
                UserEmail = conn?.UserId != null && users.TryGetValue(conn.UserId, out var email) ? email : "Unknown"
            };
        }).ToList();

        return Ok(new AdminConnectionsResponse
        {
            TotalConnections = connectionsWithOwners.Count,
            HealthyConnections = connectionsWithOwners.Count(c => c.Status == ConnectionStatus.Connected),
            FailedConnections = connectionsWithOwners.Count(c => c.Status == ConnectionStatus.Error || c.Status == ConnectionStatus.Disconnected),
            Connections = connectionsWithOwners
        });
    }

    private int GetEffectiveMaxConnections(ApplicationUser user)
    {
        return user.MaxConnections ?? _configuration.GetValue("Monitoring:DefaultUserMaxConnections", 2);
    }

}

public sealed class UserInfoResponse
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public bool IsActive { get; set; }
    public int? MaxConnections { get; set; }
    public int EffectiveMaxConnections { get; set; }
    public List<string> Roles { get; set; } = new();
    public int ConnectionCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public bool EmailConfirmed { get; set; }
}

public sealed class UpdateUserRequest
{
    public string? FullName { get; set; }
    public int? MaxConnections { get; set; }
    public bool? IsActive { get; set; }
}

public sealed class SetRoleRequest
{
    public bool IsAdmin { get; set; }
}

public sealed class AdminConnectionsResponse
{
    public int TotalConnections { get; set; }
    public int HealthyConnections { get; set; }
    public int FailedConnections { get; set; }
    public List<AdminConnectionInfo> Connections { get; set; } = new();
}

public sealed class AdminConnectionInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Server { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;
    public ConnectionStatus Status { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSuccessfulConnection { get; set; }
    public string? LastError { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
}
