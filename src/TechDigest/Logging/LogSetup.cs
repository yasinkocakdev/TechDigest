using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace TechDigest.Logging;

/// <summary>
/// Sade konsol çıktısı: kategori adı ve zaman damgası yok, sadece seviye simgesi + mesaj.
/// GitHub Actions'ta uyarı/hatalar ayrıca <c>::warning::</c>/<c>::error::</c> olarak işaretlenir,
/// böylece çalışma özetinde görünürler.
/// </summary>
public sealed class CompactFormatter : ConsoleFormatter
{
    public new const string Name = "compact";
    private readonly bool InActions;

    public CompactFormatter() : this(Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true") { }

    /// <summary>Testler için: GitHub Actions modu ortam değişkeninden bağımsız seçilebilir.</summary>
    internal CompactFormatter(bool inActions) : base(Name) => InActions = inActions;

    public override void Write<TState>(in LogEntry<TState> entry, IExternalScopeProvider? scopes, TextWriter writer)
    {
        var message = entry.Formatter(entry.State, entry.Exception);
        if (message.StartsWith(LogSetup.GroupStart, StringComparison.Ordinal))
        {
            var title = message[1..];
            writer.WriteLine(InActions ? $"::group::{title}" : $"▸ {title}");
            return;
        }
        if (string.Equals(message, LogSetup.GroupEnd, StringComparison.Ordinal))
        {
            if (InActions) writer.WriteLine("::endgroup::");
            return;
        }
        if (entry.Exception is not null) message += $" ({entry.Exception.GetType().Name}: {entry.Exception.Message})";

        writer.WriteLine(entry.LogLevel switch
        {
            LogLevel.Warning when InActions => $"::warning::{message}",
            LogLevel.Error or LogLevel.Critical when InActions => $"::error::{message}",
            LogLevel.Warning => $"  ⚠ {message}",
            LogLevel.Error or LogLevel.Critical => $"  ✗ {message}",
            LogLevel.Debug or LogLevel.Trace => $"    {message}",
            _ => $"  {message}",
        });
    }
}

public static class LogSetup
{
    public static ILoggerFactory Create(bool verbose) => LoggerFactory.Create(b => b
        .SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information)
        .AddConsole(o => o.FormatterName = CompactFormatter.Name)
        .AddConsoleFormatter<CompactFormatter, ConsoleFormatterOptions>());

    internal const string GroupStart = "\u0001", GroupEnd = "\u0002";

    /// <summary>
    /// Bir adımı log'da başlık altında toplar; GitHub Actions'ta katlanabilir grup olur.
    /// Başlık da logger kuyruğundan geçer, böylece mesajlarla sırası karışmaz.
    /// </summary>
    public static IDisposable Step(ILogger log, string title)
    {
        log.LogInformation("{Marker}{Title}", GroupStart, title);
        return new StepScope(log);
    }

    private sealed class StepScope(ILogger log) : IDisposable
    {
        public void Dispose() => log.LogInformation("{Marker}", GroupEnd);
    }

    public static ILogger<T> Null<T>() => NullLogger<T>.Instance;
}
