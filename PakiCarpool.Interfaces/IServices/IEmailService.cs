namespace PakiCarpool.Interfaces.IServices;

/// <summary>
/// Defines outbound email operations via the Brevo transactional email REST API.
/// All email sending is async and non-blocking.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends a 6-digit OTP to the user's email for verification.
    /// </summary>
    Task SendEmailOtpAsync(string toEmail, string fullName, string otp);

    /// <summary>
    /// Sends a password reset email with a secure token link.
    /// </summary>
    Task SendPasswordResetEmailAsync(string toEmail, string fullName, string resetToken);

    /// <summary>
    /// Sends a car approval notification email to the driver.
    /// </summary>
    Task SendCarApprovedEmailAsync(string toEmail, string fullName, string carMake, string carModel);

    /// <summary>
    /// Sends a car rejection notification email to the driver with reason.
    /// </summary>
    Task SendCarRejectedEmailAsync(string toEmail, string fullName, string carMake, string carModel, string reason);

    /// <summary>
    /// Sends a ride cancellation notification to the passenger.
    /// </summary>
    Task SendRideCancelledEmailAsync(string toEmail, string fullName, string rideSummary);

    /// <summary>
    /// Sends a no-show notification email to the offender.
    /// </summary>
    Task SendNoShowEmailAsync(string toEmail, string fullName);

    /// <summary>
    /// Sends a warning notification email to the user.
    /// </summary>
    Task SendWarningEmailAsync(string toEmail, string fullName, string reason);

    /// <summary>
    /// Sends an account suspension notification email.
    /// </summary>
    Task SendSuspensionEmailAsync(string toEmail, string fullName, string reason);
}
