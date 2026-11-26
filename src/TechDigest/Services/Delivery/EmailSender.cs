using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace TechDigest.Services.Delivery;

public sealed record EmailMessage(string Subject, string Html, string Text);

/// <summary>
/// Gmail SMTP üzerinden gönderir. Normal Gmail şifresi değil, "uygulama şifresi" gerekir.
/// Alıcılar BCC'ye konur: kimse diğer alıcıların adresini görmez. Geçici ağ/sunucu hatalarında
/// bir kez tekrar dener; kimlik hatasında denemez (aynı şifre yine reddedilir).
/// </summary>
public sealed class EmailSender(string gmailAddress, string appPassword, ILogger<EmailSender> log)
{
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(20);

    public async Task SendAsync(string recipients, EmailMessage email, CancellationToken ct = default)
    {
        var message = BuildMessage(gmailAddress, recipients, email);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var client = new SmtpClient { Timeout = 60_000 };
                await client.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls, ct);
                await client.AuthenticateAsync(gmailAddress, appPassword, ct);
                await client.SendAsync(message, ct);
                await client.DisconnectAsync(true, ct);
                return;
            }
            catch (Exception ex) when (attempt == 1 && ex is SmtpCommandException or SmtpProtocolException or IOException or ServiceNotConnectedException)
            {
                log.LogWarning("Gönderim başarısız ({Reason}), {Seconds} sn sonra tekrar denenecek.", ex.Message, RetryDelay.TotalSeconds);
                await Task.Delay(RetryDelay, ct);
            }
        }
    }

    public static MimeMessage BuildMessage(string from, string recipients, EmailMessage email)
    {
        var addresses = ParseRecipients(recipients);
        if (addresses.Count == 0) throw new InvalidOperationException("TO_ADDRESS içinde geçerli adres yok.");

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("TechDigest", from));
        message.To.Add(new MailboxAddress("TechDigest", from));
        foreach (var address in addresses) message.Bcc.Add(MailboxAddress.Parse(address));
        message.Subject = email.Subject;
        message.Body = new BodyBuilder { HtmlBody = email.Html, TextBody = email.Text }.ToMessageBody();
        return message;
    }

    public static List<string> ParseRecipients(string recipients) =>
        recipients.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
