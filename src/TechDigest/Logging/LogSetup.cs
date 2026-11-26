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
public sealed class CompactFormatter() : ConsoleFormatter(Name)
{
    public new const string Name = "compact";
    private static readonly bool InActions = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";

    public override void Write<TState>(in LogEntry<TState> entry, IExternalScopeProvider? scopes, TextWriter writer)
    {
        var message = entry.Formatter(entry.State, entry.Exception);
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

    /// <summary>
    /// Bir adımı log'da başlık altında toplar. GitHub Actions'ta katlanabilir grup olur.
    /// Konsol logger'ı arka planda yazdığı için grup sınırları stdout'a doğrudan yazılır.
    /// </summary>
    public static IDisposable Step(string title)
    {
        Console.Out.Flush();
        Console.WriteLine(Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true" ? $"::group::{title}" : $"▸ {title}");
        return new StepScope();
    }

    private sealed class StepScope : IDisposable
    {
        public void Dispose()
        {
            if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true") Console.WriteLine("::endgroup::");
        }
    }

    public static ILogger<T> Null<T>() => NullLogger<T>.Instance;
}
