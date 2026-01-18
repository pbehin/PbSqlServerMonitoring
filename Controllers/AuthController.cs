using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PbSqlServerMonitoring.Models;
using PbSqlServerMonitoring.Services;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace PbSqlServerMonitoring.Controllers;

[ApiController]
[Route("api/auth")]
public partial class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ILogger<AuthController> _logger;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _configuration;
    private readonly int _resetTokenMinutes;
    private readonly Dictionary<string, long> _resetTokenTimestamps = new();
    private readonly Timer _tokenCleanupTimer;

    private static readonly HashSet<string> AllowedEmailDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "gmail.com", "googlemail.com",
        "outlook.com", "hotmail.com", "live.com", "msn.com", "microsoft.com",
        "icloud.com", "me.com", "mac.com",
        "protonmail.com", "proton.me", "pm.me"
    };

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ILogger<AuthController> logger,
        IEmailService emailService,
        IConfiguration configuration)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _logger = logger;
        _emailService = emailService;
        _configuration = configuration;
        _resetTokenMinutes = _configuration.GetValue<int?>("PasswordReset:TokenLifetimeMinutes") ?? 5;

        _tokenCleanupTimer = new Timer(CleanupExpiredTokens, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    private string GetResetExpiryText()
    {
        if (_resetTokenMinutes < 60)
        {
            return _resetTokenMinutes == 1 ? "1 minute" : $"{_resetTokenMinutes} minutes";
        }

        if (_resetTokenMinutes < 60 * 24)
        {
            var hours = _resetTokenMinutes / 60;
            var remainingMinutes = _resetTokenMinutes % 60;

            if (remainingMinutes == 0)
            {
                return hours == 1 ? "1 hour" : $"{hours} hours";
            }

            var hourText = hours == 1 ? "1 hour" : $"{hours} hours";
            var minuteText = remainingMinutes == 1 ? "1 minute" : $"{remainingMinutes} minutes";
            return $"{hourText} {minuteText}";
        }

        var days = _resetTokenMinutes / (60 * 24);
        var remainingHours = (_resetTokenMinutes % (60 * 24)) / 60;

        if (remainingHours == 0)
        {
            return days == 1 ? "1 day" : $"{days} days";
        }

        var dayText = days == 1 ? "1 day" : $"{days} days";
        var hourText2 = remainingHours == 1 ? "1 hour" : $"{remainingHours} hours";
        return $"{dayText} {hourText2}";
    }

    /// <summary>
    /// Cleanup expired tokens to prevent memory leaks and improve performance
    /// </summary>
    private void CleanupExpiredTokens(object? state)
    {
        var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var expiryThreshold = currentTime - (_resetTokenMinutes * 60);

        var expiredTokens = _resetTokenTimestamps
            .Where(kvp => kvp.Value < expiryThreshold)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var token in expiredTokens)
        {
            _resetTokenTimestamps.Remove(token);
        }

        if (expiredTokens.Count > 0)
        {
            _logger.LogInformation("Cleaned up {Count} expired reset tokens", expiredTokens.Count);
        }
    }

    /// <summary>
    /// Validates that email is from an allowed provider.
    /// </summary>
    private static bool IsAllowedEmailDomain(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;

        var atIndex = email.LastIndexOf('@');
        if (atIndex < 0 || atIndex >= email.Length - 1) return false;

        var domain = email[(atIndex + 1)..];
        return AllowedEmailDomains.Contains(domain);
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { error = "Email and Password are required." });

        if (!IsAllowedEmailDomain(request.Email))
        {
            return BadRequest(new { error = "Please use an email from a major provider (Gmail, Outlook, iCloud, or ProtonMail)." });
        }

        var user = new ApplicationUser { UserName = request.Email, Email = request.Email, FullName = request.FullName };
        var result = await _userManager.CreateAsync(user, request.Password);

        if (result.Succeeded)
        {
            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            var confirmUrl = Url.Action(nameof(ConfirmEmail), "Auth", new { userId = user.Id, token }, Request.Scheme);

            try
            {
                await _emailService.SendEmailConfirmationAsync(user.Email!, confirmUrl!);
                _logger.LogInformation("Email confirmation sent to {Email}", user.Email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send confirmation email to {Email}. Confirmation URL: {Url}", user.Email, confirmUrl);
            }

            return Ok(new {
                message = "Registration successful. Please check your email to confirm your account.",
                requiresEmailConfirmation = true
            });
        }

        return BadRequest(new { error = "Registration failed", details = result.Errors.Select(e => e.Description) });
    }

    [HttpGet("confirm-email")]
    public async Task<IActionResult> ConfirmEmail(string userId, string token)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(token))
            return BadRequest(new { error = "Invalid confirmation link." });

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
            return NotFound(new { error = "User not found." });

        var result = await _userManager.ConfirmEmailAsync(user, token);
        if (result.Succeeded)
        {
            return Redirect("/?emailConfirmed=true");
        }

        return BadRequest(new { error = "Email confirmation failed. The link may have expired." });
    }

    [HttpPost("resend-confirmation")]
    public async Task<IActionResult> ResendConfirmation([FromBody] ResendConfirmationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { error = "Email is required." });

        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null)
        {
            return Ok(new { message = "If an account exists, a confirmation email will be sent." });
        }

        if (await _userManager.IsEmailConfirmedAsync(user))
        {
            return BadRequest(new { error = "Email is already confirmed." });
        }

        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        var confirmUrl = Url.Action(nameof(ConfirmEmail), "Auth", new { userId = user.Id, token }, Request.Scheme);

        try
        {
            await _emailService.SendEmailConfirmationAsync(user.Email!, confirmUrl!);
            _logger.LogInformation("Resent confirmation email to {Email}", user.Email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resend confirmation email to {Email}", user.Email);
        }

        return Ok(new {
            message = "If an account exists, a confirmation email will be sent."
        });
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { error = "Email is required." });

        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null)
        {
            return Ok(new { message = "If an account exists, a password reset email will be sent." });
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var resetUrl = Url.Action(nameof(ResetPassword), "Auth", new { userId = user.Id, token, expiresMinutes = _resetTokenMinutes, timestamp }, Request.Scheme);

        try
        {
            await _emailService.SendPasswordResetAsync(user.Email!, resetUrl!);
            _logger.LogInformation("Password reset email sent to {Email}", user.Email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send password reset email to {Email}", user.Email);
        }

        return Ok(new {
            message = $"If an account exists, a password reset email will be sent. The link expires in {GetResetExpiryText()}.",
            expiresMinutes = _resetTokenMinutes
        });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.NewPassword))
            return BadRequest(new { error = "Token and new password are required." });

        ApplicationUser? user = null;

        if (!string.IsNullOrWhiteSpace(request.UserId))
        {
            user = await _userManager.FindByIdAsync(request.UserId);
        }
        else if (!string.IsNullOrWhiteSpace(request.Email))
        {
            user = await _userManager.FindByEmailAsync(request.Email);
        }

        if (user == null)
            return BadRequest(new { error = "Invalid reset token or user identifier." });

        var result = await _userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (result.Succeeded)
        {
            _resetTokenTimestamps.Remove(request.Token);
            return Ok(new { message = "Password has been reset successfully. You can now login with your new password." });
        }

        return BadRequest(new { error = $"Password reset failed. The link may have expired or is invalid. Reset links expire after {GetResetExpiryText()}. Please request a new reset link.", details = result.Errors.Select(e => e.Description) });
    }

    [HttpGet("reset-password")]
    public IActionResult ResetPasswordPage(string userId, string token)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(token))
            return BadRequest("Invalid reset link.");

        if (_resetTokenTimestamps.TryGetValue(token, out var timestamp))
        {
            var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var expiryTime = timestamp + (_resetTokenMinutes * 60);

            if (currentTime > expiryTime)
            {
                _resetTokenTimestamps.Remove(token);
                return BadRequest("Reset link has expired. Please request a new password reset.");
            }
        }
        else
        {
            _logger.LogWarning("Reset token not found for user {UserId}", userId);
        }

        return Redirect($"/?resetPassword=true&userId={Uri.EscapeDataString(userId)}&token={Uri.EscapeDataString(token)}&expiresMinutes={_resetTokenMinutes}");
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user != null && !await _userManager.IsEmailConfirmedAsync(user))
        {
            return BadRequest(new {
                error = "Please confirm your email before logging in.",
                requiresEmailConfirmation = true
            });
        }

        var result = await _signInManager.PasswordSignInAsync(request.Email, request.Password, request.RememberMe, lockoutOnFailure: false);

        if (result.Succeeded)
        {
            return Ok(new { message = "Login successful" });
        }

        if (result.IsNotAllowed)
        {
            return BadRequest(new {
                error = "Please confirm your email before logging in.",
                requiresEmailConfirmation = true
            });
        }

        return Unauthorized(new { error = "Invalid login attempt" });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return Ok(new { message = "Logged out" });
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Unauthorized(new { error = "Not authenticated" });
        }

        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        return Ok(new
        {
            isAuthenticated = true,
            email = user.Email,
            fullName = user.FullName,
            id = user.Id
        });
    }

    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetUserById(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return BadRequest(new { error = "User ID is required." });

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
            return NotFound(new { error = "User not found." });

        return Ok(new
        {
            id = user.Id,
            email = user.Email,
            fullName = user.FullName
        });
    }

    [HttpGet("login-{provider}")]
    public IActionResult ExternalLogin(string provider, string? returnUrl = null)
    {
        var redirectUrl = Url.Action(nameof(ExternalLoginCallback), "Auth", new { returnUrl });
        var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
        return Challenge(properties, provider);
    }

    [HttpGet("callback")]
    public async Task<IActionResult> ExternalLoginCallback(string? returnUrl = null, string? remoteError = null)
    {
        if (remoteError != null)
        {
            _logger.LogWarning("External login error: {Error}", remoteError);
            return Redirect($"/?error={Uri.EscapeDataString($"External provider error: {remoteError}")}");
        }

        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info == null)
        {
            _logger.LogError("Failed to load external login information");
            return Redirect($"/?error={Uri.EscapeDataString("Failed to load external login information. Please try again.")}");
        }

        var result = await _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: true);

        if (result.Succeeded)
        {
            _logger.LogInformation("External login succeeded for provider {Provider}", info.LoginProvider);
            return Redirect(returnUrl ?? "/");
        }

        if (result.IsLockedOut)
        {
            _logger.LogWarning("Account locked out during external login");
            return Redirect($"/?error={Uri.EscapeDataString("Account is locked out. Please contact support.")}");
        }
        else
        {
            var email = info.Principal.FindFirstValue(ClaimTypes.Email);
            var name = info.Principal.FindFirstValue(ClaimTypes.Name);

            if (email == null)
            {
                _logger.LogError("Email claim not received from external provider {Provider}", info.LoginProvider);
                return Redirect($"/?error={Uri.EscapeDataString("Email not provided by authentication provider. Please try a different account.")}");
            }

            if (!IsAllowedEmailDomain(email))
            {
                _logger.LogWarning("Email domain not allowed: {Email}", email);
                return Redirect($"/?error={Uri.EscapeDataString("Email domain not allowed. Please use an email from a major provider (Gmail, Outlook, iCloud, or ProtonMail).")}");
            }

            var existingUser = await _userManager.FindByEmailAsync(email);
            if (existingUser != null)
            {
                var addLoginResult = await _userManager.AddLoginAsync(existingUser, info);
                if (addLoginResult.Succeeded)
                {
                    await _signInManager.SignInAsync(existingUser, isPersistent: false);
                    _logger.LogInformation("Added external login to existing user {Email}", email);
                    return Redirect(returnUrl ?? "/");
                }
                else
                {
                    _logger.LogWarning("Failed to add external login to existing user {Email}: {Errors}",
                        email, string.Join(", ", addLoginResult.Errors.Select(e => e.Description)));
                    return Redirect($"/?error={Uri.EscapeDataString("An account with this email already exists. Please sign in with your password first.")}");
                }
            }

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FullName = name,
                EmailConfirmed = true
            };
            var createResult = await _userManager.CreateAsync(user);

            if (createResult.Succeeded)
            {
                createResult = await _userManager.AddLoginAsync(user, info);
                if (createResult.Succeeded)
                {
                    await _signInManager.SignInAsync(user, isPersistent: false);
                    _logger.LogInformation("Created new user from external login: {Email}", email);
                    return Redirect(returnUrl ?? "/");
                }
                else
                {
                    _logger.LogError("Failed to add external login after user creation: {Errors}",
                        string.Join(", ", createResult.Errors.Select(e => e.Description)));
                }
            }
            else
            {
                _logger.LogError("Failed to create user from external login: {Errors}",
                    string.Join(", ", createResult.Errors.Select(e => e.Description)));
            }

            return Redirect($"/?error={Uri.EscapeDataString("Failed to create account. Please try again or register with email and password.")}");
        }
    }
}

public class RegisterRequest
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string? FullName { get; set; }
}

public class LoginRequest
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public bool RememberMe { get; set; }
}

public class ResendConfirmationRequest
{
    public string Email { get; set; } = "";
}

public class ForgotPasswordRequest
{
    public string Email { get; set; } = "";
}

public class ResetPasswordRequest
{
    public string? UserId { get; set; }
    public string? Email { get; set; }
    public string Token { get; set; } = "";
    public string NewPassword { get; set; } = "";
}
