using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TechDigest.Logging;

namespace TechDigest.Tests;

public class LoggingTests
{
    private static string Format(string message, LogLevel level = LogLevel.Information)
    {
        var writer = new StringWriter();
        var entry = new LogEntry<string>(level, "c", default, message, null, (s, _) => s);
        new CompactFormatter().Write(entry, null, writer);
        return writer.ToString().TrimEnd('\r', '\n');
    }

    [Fact]
    public void PlainMessage_IsIndented_NotTreatedAsGroup() =>
        Assert.Equal("  Kaynak atlandı", Format("Kaynak atlandı"));

    [Fact]
    public void GroupMarkers_BecomeHeaders() =>
        Assert.Equal("▸ 1/5 Haberler", Format(LogSetup.GroupStart + "1/5 Haberler"));

    [Fact]
    public void Warning_HasSymbol() => Assert.Equal("  ⚠ dikkat", Format("dikkat", LogLevel.Warning));
}
