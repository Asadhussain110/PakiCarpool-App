using System.Globalization;
using System.Net;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;
using PakiCarpool.Interfaces.IServices;

namespace PakiCarpool.Services.Implementations;

/// <summary>
/// Sends transactional emails through the configured SMTP server.
/// All credentials are read from configuration — never hardcoded.
/// </summary>
public class EmailService : IEmailService
{
    private readonly string _smtpHost;
    private readonly int _smtpPort;
    private readonly string _smtpUser;
    private readonly string _smtpPass;
    private readonly string _fromEmail;

    public EmailService(IConfiguration configuration)
    {
        _smtpHost = GetRequiredSetting(configuration, "SMTP_HOST");
        var smtpPort = GetRequiredSetting(configuration, "SMTP_PORT");
        if (!int.TryParse(smtpPort, NumberStyles.None, CultureInfo.InvariantCulture, out var smtpPortNumber)
            || smtpPortNumber is < 1 or > 65535)
        {
            throw new InvalidOperationException("SMTP_PORT must be a valid TCP port between 1 and 65535.");
        }
        _smtpPort = smtpPortNumber;

        _smtpUser = GetRequiredSetting(configuration, "SMTP_USER");
        _smtpPass = GetRequiredSetting(configuration, "SMTP_PASS");
        _fromEmail = GetRequiredSetting(configuration, "SMTP_FROM");
    }

    /// <summary>
    /// Sends a 6-digit OTP to the user's email for verification.
    /// </summary>
    public async Task SendEmailOtpAsync(string toEmail, string fullName, string otp)
    {
        var subject = "Your Paki Carpool Verification Code";
        var body = $@"
<html><body>
<h2>Hello {WebUtility.HtmlEncode(fullName)},</h2>
<p>Your email verification code is:</p>
<h1 style='letter-spacing:8px;font-size:32px;color:#2563EB;'>{WebUtility.HtmlEncode(otp)}</h1>
<p>This code expires in <strong>10 minutes</strong>. Do not share it with anyone.</p>
<p>— Paki Carpool Team</p>
</body></html>";

        await SendAsync(toEmail, fullName, subject, body);
    }

    /// <summary>
    /// Sends a short-lived password reset code.
    /// </summary>
    public async Task SendPasswordResetEmailAsync(string toEmail, string fullName, string resetCode)
    {
        var subject = "Reset Your Paki Carpool Password";
        var body = $@"
<html><body>
<h2>Hello {WebUtility.HtmlEncode(fullName)},</h2>
<p>Enter this code in the PakiCarpool app to reset your password:</p>
<h1 style='letter-spacing:8px;font-size:32px;color:#2563EB;'>{WebUtility.HtmlEncode(resetCode)}</h1>
<p>This code expires in <strong>10 minutes</strong> and can only be used once. If you did not request this, please ignore this email.</p>
<p>— Paki Carpool Team</p>
</body></html>";

        await SendAsync(toEmail, fullName, subject, body);
    }

    /// <summary>
    /// Sends a short-lived administrator password reset code.
    /// </summary>
    public async Task SendAdminPasswordResetCodeAsync(string toEmail, string fullName, string code)
    {
        var subject = "Your Paki Carpool Admin Password Reset Code";
        var body = $@"
<html><body>
<h2>Hello {WebUtility.HtmlEncode(fullName)},</h2>
<p>Use this code in the Paki Carpool Admin app to reset your password:</p>
<h1 style='letter-spacing:8px;font-size:32px;color:#2563EB;'>{WebUtility.HtmlEncode(code)}</h1>
<p>This code expires in <strong>10 minutes</strong> and can only be used once. Your authenticator code is also required.</p>
<p>If you did not request this, please ignore this email.</p>
<p>— Paki Carpool Team</p>
</body></html>";

        await SendAsync(toEmail, fullName, subject, body);
    }

    /// <summary>
    /// Sends a car approval notification email to the driver.
    /// </summary>
    public async Task SendCarApprovedEmailAsync(string toEmail, string fullName, string carMake, string carModel)
    {
        var subject = "Your Car Has Been Approved — Paki Carpool";
        var body = $@"
<html><body>
<h2>Congratulations, {WebUtility.HtmlEncode(fullName)}!</h2>
<p>Your <strong>{WebUtility.HtmlEncode(carMake)} {WebUtility.HtmlEncode(carModel)}</strong> has been approved by our team.</p>
<p>You can now post rides and start earning. Happy driving!</p>
<p>— Paki Carpool Team</p>
</body></html>";

        await SendAsync(toEmail, fullName, subject, body);
    }

    /// <summary>
    /// Sends a car rejection notification email to the driver with reason.
    /// </summary>
    public async Task SendCarRejectedEmailAsync(string toEmail, string fullName, string carMake, string carModel, string reason)
    {
        var subject = "Car Registration Update — Paki Carpool";
        var body = $@"
<html><body>
<h2>Hello {WebUtility.HtmlEncode(fullName)},</h2>
<p>We were unable to approve your <strong>{WebUtility.HtmlEncode(carMake)} {WebUtility.HtmlEncode(carModel)}</strong> at this time.</p>
<p><strong>Reason:</strong> {WebUtility.HtmlEncode(reason)}</p>
<p>Please update your car details and resubmit for review. If you have questions, contact our support team.</p>
<p>— Paki Carpool Team</p>
</body></html>";

        await SendAsync(toEmail, fullName, subject, body);
    }

    /// <summary>
    /// Sends a ride cancellation notification to the passenger.
    /// </summary>
    public async Task SendRideCancelledEmailAsync(string toEmail, string fullName, string rideSummary)
    {
        var subject = "Your Ride Has Been Cancelled — Paki Carpool";
        var body = $@"
<html><body>
<h2>Hello {WebUtility.HtmlEncode(fullName)},</h2>
<p>Unfortunately, your upcoming ride has been cancelled by the driver.</p>
<p><strong>Ride:</strong> {WebUtility.HtmlEncode(rideSummary)}</p>
<p>We apologize for the inconvenience. Please browse available rides in the app.</p>
<p>— Paki Carpool Team</p>
</body></html>";

        await SendAsync(toEmail, fullName, subject, body);
    }

    /// <summary>
    /// Sends a no-show notification email to the offender.
    /// </summary>
    public async Task SendNoShowEmailAsync(string toEmail, string fullName)
    {
        var subject = "No-Show Recorded — Paki Carpool";
        var body = $@"
<html><body>
<h2>Hello {WebUtility.HtmlEncode(fullName)},</h2>
<p>A no-show has been recorded against your account on a recent ride.</p>
<p>Repeated no-shows will affect your trust score and may result in account suspension.</p>
<p>Please ensure you are available at the agreed pickup point on time.</p>
<p>— Paki Carpool Team</p>
</body></html>";

        await SendAsync(toEmail, fullName, subject, body);
    }

    /// <summary>
    /// Sends a warning notification email to the user.
    /// </summary>
    public async Task SendWarningEmailAsync(string toEmail, string fullName, string reason)
    {
        var subject = "Account Warning — Paki Carpool";
        var body = $@"
<html><body>
<h2>Hello {WebUtility.HtmlEncode(fullName)},</h2>
<p>Your account has received a formal warning.</p>
<p><strong>Reason:</strong> {WebUtility.HtmlEncode(reason)}</p>
<p>Continued violations may result in account suspension. Please review our community guidelines.</p>
<p>— Paki Carpool Team</p>
</body></html>";

        await SendAsync(toEmail, fullName, subject, body);
    }

    /// <summary>
    /// Sends an account suspension notification email.
    /// </summary>
    public async Task SendSuspensionEmailAsync(string toEmail, string fullName, string reason)
    {
        var subject = "Account Suspended — Paki Carpool";
        var body = $@"
<html><body>
<h2>Hello {WebUtility.HtmlEncode(fullName)},</h2>
<p>Your Paki Carpool account has been suspended.</p>
<p><strong>Reason:</strong> {WebUtility.HtmlEncode(reason)}</p>
<p>If you believe this is an error, please contact our support team for review.</p>
<p>— Paki Carpool Team</p>
</body></html>";

        await SendAsync(toEmail, fullName, subject, body);
    }

    private async Task SendAsync(string toEmail, string toName, string subject, string htmlBody)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("PakiCarpool", _fromEmail));
        message.To.Add(new MailboxAddress(toName, toEmail));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(_smtpHost, _smtpPort, SecureSocketOptions.StartTls);
        await client.AuthenticateAsync(_smtpUser, _smtpPass);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }

    private static string GetRequiredSetting(IConfiguration configuration, string key)
    {
        var value = configuration[key] ?? Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{key} is not configured.")
            : value;
    }
}
