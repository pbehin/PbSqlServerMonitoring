using System.Net;
using System.Net.Mail;

namespace PbSqlServerMonitoring.Services;

/// <summary>
/// Email service implementation using SMTP.
/// Supports common providers: Gmail, Outlook, etc.
/// </summary>
public class SmtpEmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SmtpEmailService> _logger;
    
    public SmtpEmailService(IConfiguration configuration, ILogger<SmtpEmailService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }
    
    public async Task SendEmailAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        var smtpHost = _configuration["Email:SmtpHost"];
        var smtpPortStr = _configuration["Email:SmtpPort"];
        var smtpUser = _configuration["Email:SmtpUser"];
        var smtpPassword = _configuration["Email:SmtpPassword"];
        var fromEmail = _configuration["Email:FromEmail"];
        var fromName = _configuration["Email:FromName"] ?? "SQL Server Monitor";
        
        // Validate configuration
        if (string.IsNullOrEmpty(smtpHost) || string.IsNullOrEmpty(smtpUser) || 
            string.IsNullOrEmpty(smtpPassword) || string.IsNullOrEmpty(fromEmail))
        {
            _logger.LogWarning(
                "Email service not configured. Add Email:SmtpHost, Email:SmtpPort, Email:SmtpUser, " +
                "Email:SmtpPassword, and Email:FromEmail to appsettings.json. " +
                "Email to {To} with subject '{Subject}' was NOT sent.", to, subject);
            return;
        }
        
        if (!int.TryParse(smtpPortStr, out var smtpPort))
        {
            smtpPort = 587; // Default TLS port
        }
        
        try
        {
            using var client = new SmtpClient(smtpHost, smtpPort)
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(smtpUser, smtpPassword),
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 30000 // 30 seconds
            };
            
            using var message = new MailMessage
            {
                From = new MailAddress(fromEmail, fromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            message.To.Add(to);
            
            await client.SendMailAsync(message, cancellationToken);
            _logger.LogInformation("Email sent successfully to {To}: {Subject}", to, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To}: {Subject}", to, subject);
            throw;
        }
    }
    
    public async Task SendEmailConfirmationAsync(string email, string confirmationUrl, CancellationToken cancellationToken = default)
    {
        var appName = _configuration["Email:AppName"] ?? "SQL Server Monitor";
        
        var subject = $"Confirm your {appName} account";
        
        var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
</head>
<body style=""font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; margin: 0; padding: 20px; background-color: #f5f5f5;"">
    <div style=""max-width: 600px; margin: 0 auto; background-color: #ffffff; border-radius: 8px; padding: 40px; box-shadow: 0 2px 4px rgba(0,0,0,0.1);"">
        <h1 style=""color: #1f2937; margin-bottom: 24px; font-size: 24px;"">Welcome to {appName}!</h1>
        
        <p style=""color: #4b5563; font-size: 16px; line-height: 1.6; margin-bottom: 24px;"">
            Thank you for registering. Please confirm your email address by clicking the button below:
        </p>
        
        <div style=""text-align: center; margin: 32px 0;"">
            <a href=""{confirmationUrl}"" 
               style=""display: inline-block; background-color: #6366f1; color: #ffffff; text-decoration: none; 
                      padding: 14px 32px; border-radius: 8px; font-weight: 600; font-size: 16px;"">
                Confirm Email Address
            </a>
        </div>
        
        <p style=""color: #6b7280; font-size: 14px; line-height: 1.6; margin-bottom: 16px;"">
            If you didn't create an account, you can safely ignore this email.
        </p>
        
        <p style=""color: #9ca3af; font-size: 12px; line-height: 1.6; border-top: 1px solid #e5e7eb; padding-top: 16px; margin-top: 32px;"">
            If the button doesn't work, copy and paste this URL into your browser:<br>
            <a href=""{confirmationUrl}"" style=""color: #6366f1; word-break: break-all;"">{confirmationUrl}</a>
        </p>
    </div>
</body>
</html>";
        
        await SendEmailAsync(email, subject, htmlBody, cancellationToken);
    }
    
    public async Task SendPasswordResetAsync(string email, string resetUrl, CancellationToken cancellationToken = default)
    {
        var appName = _configuration["Email:AppName"] ?? "SQL Server Monitor";
        var resetMinutes = _configuration.GetValue<int?>("PasswordReset:TokenLifetimeMinutes") ?? 5;

        string GetExpiryText()
        {
            if (resetMinutes % 60 == 0)
            {
                var hours = resetMinutes / 60;
                if (hours % 24 == 0)
                {
                    var days = hours / 24;
                    return days == 1 ? "1 day" : $"{days} days";
                }
                return hours == 1 ? "1 hour" : $"{hours} hours";
            }
            return resetMinutes == 1 ? "1 minute" : $"{resetMinutes} minutes";
        }

        var expiryText = GetExpiryText();
        
        var subject = $"Reset your {appName} password";
        
        var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
</head>
<body style=""font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; margin: 0; padding: 20px; background-color: #f5f5f5;"">
    <div style=""max-width: 600px; margin: 0 auto; background-color: #ffffff; border-radius: 8px; padding: 40px; box-shadow: 0 2px 4px rgba(0,0,0,0.1);"">
        <h1 style=""color: #1f2937; margin-bottom: 24px; font-size: 24px;"">Password Reset Request</h1>
        
        <p style=""color: #4b5563; font-size: 16px; line-height: 1.6; margin-bottom: 24px;"">
            We received a request to reset your {appName} password. Click the button below to set a new password:
        </p>
        
        <div style=""text-align: center; margin: 32px 0;"">
            <a href=""{resetUrl}"" 
               style=""display: inline-block; background-color: #6366f1; color: #ffffff; text-decoration: none; 
                      padding: 14px 32px; border-radius: 8px; font-weight: 600; font-size: 16px;"">
                Reset Password
            </a>
        </div>
        
        <p style=""color: #6b7280; font-size: 14px; line-height: 1.6; margin-bottom: 16px;"">
            If you didn't request a password reset, you can safely ignore this email. Your password will not be changed.
        </p>
        
        <p style=""color: #9ca3af; font-size: 12px; line-height: 1.6; border-top: 1px solid #e5e7eb; padding-top: 16px; margin-top: 32px;"">
            <strong>Note:</strong> This link will expire in {expiryText} for security reasons.<br><br>
            If the button doesn't work, copy and paste this URL into your browser:<br>
            <a href=""{resetUrl}"" style=""color: #6366f1; word-break: break-all;"">{resetUrl}</a>
        </p>
    </div>
</body>
</html>";
        
        await SendEmailAsync(email, subject, htmlBody, cancellationToken);
    }
}
