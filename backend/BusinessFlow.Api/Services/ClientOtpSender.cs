using System.Net;
using System.Net.Mail;

namespace BusinessFlow.Api.Services;

public interface IClientOtpSender
{
    bool IsConfigured { get; }
    Task SendAsync(string email, string code, CancellationToken cancellationToken);
}

public sealed class SmtpClientOtpSender(IConfiguration config) : IClientOtpSender
{
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(config["ClientPortal:Mail:Host"]) &&
        int.TryParse(config["ClientPortal:Mail:Port"], out var port) && port is > 0 and <= 65535 &&
        !string.IsNullOrWhiteSpace(config["ClientPortal:Mail:User"]) &&
        !string.IsNullOrWhiteSpace(config["ClientPortal:Mail:Password"]) &&
        !string.IsNullOrWhiteSpace(config["ClientPortal:Mail:From"]);

    public async Task SendAsync(string email, string code, CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new InvalidOperationException("Client OTP email delivery is not configured.");

        using var message = new MailMessage(
            new MailAddress(config["ClientPortal:Mail:From"]!),
            new MailAddress(email))
        {
            Subject = "Resmoh IT — your sign-in code",
            Body = $"Your Resmoh IT sign-in code is {code}. It expires in 10 minutes. Do not share this code."
        };
        using var smtp = new SmtpClient(
            config["ClientPortal:Mail:Host"]!,
            int.Parse(config["ClientPortal:Mail:Port"]!))
        {
            EnableSsl = true,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(
                config["ClientPortal:Mail:User"],
                config["ClientPortal:Mail:Password"])
        };
        await smtp.SendMailAsync(message, cancellationToken);
    }
}
