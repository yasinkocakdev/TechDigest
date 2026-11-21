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
    /// <param name="recipients">Virgül veya noktalı virgülle ayrılmış adresler: "ben@x.com, arkadas@y.com"</param>
    public async Task SendAsync(string recipients, string subject, string html, string text, CancellationToken ct = default)
    {
        var message = BuildMessage(gmailAddress, recipients, subject, html, text);

        using var client = new SmtpClient();
        await client.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls, ct);
        await client.AuthenticateAsync(gmailAddress, appPassword, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }

    /// <summary>
    /// Alıcılar BCC'ye konur: kimse diğer alıcıların adresini göremez.
    /// "To" alanında gönderen adres durur.
    /// </summary>
    public static MimeMessage BuildMessage(string from, string recipients, string subject, string html, string text)
    {
        var addresses = ParseRecipients(recipients);
        if (addresses.Count == 0)
            throw new InvalidOperationException("TO_ADDRESS içinde geçerli adres yok.");

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("TechDigest", from));
        message.To.Add(new MailboxAddress("TechDigest", from));
        foreach (var address in addresses)
            message.Bcc.Add(MailboxAddress.Parse(address));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = html, TextBody = text }.ToMessageBody();
        return message;
    }

    public static List<string> ParseRecipients(string recipients) =>
        recipients.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
