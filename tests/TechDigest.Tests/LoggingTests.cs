using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TechDigest.Logging;

namespace TechDigest.Tests;

public class LoggingTests
{
    private static string Format(string message, LogLevel level = LogLevel.Information, bool inActions = false)
    {
        var writer = new StringWriter();
        var entry = new LogEntry<string>(level, "c", default, message, null, (s, _) => s);
        new CompactFormatter(inActions).Write(entry, null, writer);
        return writer.ToString().TrimEnd('\r', '\n');
    }

    [Fact]
    public void PlainMessage_IsIndented_NotTreatedAsGroup() =>
        Assert.Equal("  Kaynak atlandı", Format("Kaynak atlandı"));

    [Theory]
    [InlineData(false, "▸ 1/5 Haberler")]
    [InlineData(true, "::group::1/5 Haberler")]
    public void GroupStart(bool inActions, string expected) =>
        Assert.Equal(expected, Format(LogSetup.GroupStart + "1/5 Haberler", inActions: inActions));

    [Theory]
    [InlineData(false, "")]
    [InlineData(true, "::endgroup::")]
    public void GroupEnd(bool inActions, string expected) =>
        Assert.Equal(expected, Format(LogSetup.GroupEnd, inActions: inActions));

    [Theory]
    [InlineData(false, "  ⚠ dikkat")]
    [InlineData(true, "::warning::dikkat")]
    public void Warning(bool inActions, string expected) =>
        Assert.Equal(expected, Format("dikkat", LogLevel.Warning, inActions));
}
