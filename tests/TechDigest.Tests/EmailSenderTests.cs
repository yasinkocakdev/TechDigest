using TechDigest.Services;

namespace TechDigest.Tests;

public class EmailSenderTests
{
    [Fact]
    public void ParseRecipients_SplitsTrimsAndDedupes() =>
        Assert.Equal(["me@a.com", "friend@b.com"],
            EmailSender.ParseRecipients(" me@a.com, friend@b.com ;ME@a.com,"));

    [Fact]
    public void BuildMessage_PutsRecipientsInBcc_SenderInTo()
    {
        var message = EmailSender.BuildMessage("sender@gmail.com", "me@a.com, friend@b.com", "Subj", "<p>x</p>", "x");

        Assert.Equal(["sender@gmail.com"], message.To.Mailboxes.Select(m => m.Address));
        Assert.Equal(["me@a.com", "friend@b.com"], message.Bcc.Mailboxes.Select(m => m.Address));
    }

    [Fact]
    public void BuildMessage_ThrowsWhenNoRecipients() =>
        Assert.Throws<InvalidOperationException>(() =>
            EmailSender.BuildMessage("sender@gmail.com", " , ", "Subj", "", ""));
}
