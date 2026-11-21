using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace TechDigest.Services;

/// <summary>
/// Gmail SMTP üzerinden mail gönderir. Normal Gmail şifresi değil, "uygulama şifresi" gerekir
/// (Google Hesabı → Güvenlik → 2 Adımlı Doğrulama → Uygulama şifreleri).
/// </summary>
public sealed class EmailSender(string gmailAddress, string appPassword)
{
    public async Task SendAsync(string to, string subject, string html, string text, CancellationToken ct = default)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("TechDigest", gmailAddress));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = html, TextBody = text }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls, ct);
        await client.AuthenticateAsync(gmailAddress, appPassword, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}
