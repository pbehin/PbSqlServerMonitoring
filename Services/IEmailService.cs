namespace PbSqlServerMonitoring.Services;

/// <summary>
/// Service interface for sending emails.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends an email asynchronously.
    /// </summary>
    /// <param name="to">Recipient email address.</param>
    /// <param name="subject">Email subject.</param>
    /// <param name="htmlBody">HTML body content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendEmailAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Sends an email confirmation link to a user.
    /// </summary>
    /// <param name="email">User's email address.</param>
    /// <param name="confirmationUrl">The confirmation URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendEmailConfirmationAsync(string email, string confirmationUrl, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Sends a password reset link to a user.
    /// </summary>
    /// <param name="email">User's email address.</param>
    /// <param name="resetUrl">The password reset URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendPasswordResetAsync(string email, string resetUrl, CancellationToken cancellationToken = default);
}
