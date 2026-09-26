using Microsoft.Extensions.Configuration;
using PakiCarpool.Interfaces.IServices;
using System.Net.Http.Json;

namespace PakiCarpool.Services.Implementations;

/// <summary>
/// Sends transactional emails through the Brevo REST API.
/// All credentials are read from configuration — never hardcoded.
/// </summary>
public class EmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _apiKey;
    private readonly string _fromEmail;
    private const string BrevoEmailEndpoint = "https://api.brevo.com/v3/smtp/email";

    public EmailService(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _apiKey = _configuration["BREVO_API_KEY"]
            ?? throw new InvalidOperationException("BREVO_API_KEY is not configured.");
        _fromEmail = _configuration["FROM_EMAIL"]
            ?? throw new InvalidOperationException("FROM_EMAIL is not configured.");
    }

    /// <summary>
    /// Sends a 6-digit OTP to the user's email for verification.
    /// </summary>
    public async Task SendEmailOtpAsync(string toEmail, string fullName, string otp)
    {
        var subject = "Your Paki Carpool Verification Code";
        var body = $@"
<html><body>
<h2>Hello {fullName},</h2>
<p>Your email verification code is:</p>
<h1 style='letter-spacing:8px;font-size:32px;color:#2563EB;'>{otp}</h1>
<p>This code expires in <strong>10 minutes</strong>. Do not share it with anyone.</p>
<p>— Paki Carpool Team</p>
</body></html>";

        await SendAsync(toEmail, fullName, subject, body);
    }

    /// <summary>
    /// Sends a password reset email with a secure token link.
    /// </summary>
    public async Task SendPasswordResetEmailAsync(string toEmail, string fullName, string resetToken)
    {
        var appDomain = _configuration["APP_DOMAIN"] ?? "app.pakicarpool.pk";
        var resetLink = $"https://{appDomain}/reset-password?token={Uri.EscapeDataString(resetToken)}&email={Uri.EscapeDataString(toEmail)}";
        var subject = "Reset Your Paki Carpool Password";
        var body = $@"
<html><body>
<h2>Hello {fullName},</h2>
<p>We received a request to reset your password. Click the link below to continue:</p>
<p><a href='{resetLink}' style='color:#2563EB;'>Reset Password</a></p>
<p>This link expires in <strong>30 minutes</strong>. If you did not request this, please ignore this email.</p>
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
<h2>Congratulations, {fullName}!</h2>
<p>Your <strong>{carMake} {carModel}</strong> has been approved by our team.</p>
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
<h2>Hello {fullName},</h2>
<p>We were unable to approve your <strong>{carMake} {carModel}</strong> at this time.</p>
<p><strong>Reason:</strong> {reason}</p>
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
<h2>Hello {fullName},</h2>
<p>Unfortunately, your upcoming ride has been cancelled by the driver.</p>
<p><strong>Ride:</strong> {rideSummary}</p>
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
<h2>Hello {fullName},</h2>
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
<h2>Hello {fullName},</h2>
<p>Your account has received a formal warning.</p>
<p><strong>Reason:</strong> {reason}</p>
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
<h2>Hello {fullName},</h2>
<p>Your Paki Carpool account has been suspended.</p>
<p><strong>Reason:</strong> {reason}</p>
<p>If you believe this is an error, please contact our support team for review.</p>
<p>— Paki Carpool Team</p>
</body></html>";

        await SendAsync(toEmail, fullName, subject, body);
    }

    // ─── Private Helper ────────────────────────────────────────────────────────

    private async Task SendAsync(string toEmail, string toName, string subject, string htmlBody)
    {
        var client = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, BrevoEmailEndpoint)
        {
            Content = JsonContent.Create(new
            {
                sender = new
                {
                    name = "PakiCarpool",
                    email = _fromEmail
                },
                to = new[]
                {
                    new
                    {
                        email = toEmail,
                        name = toName
                    }
                },
                subject,
                htmlContent = htmlBody
            })
        };

        request.Headers.Add("api-key", _apiKey);

        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
}
