using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace TrickFireDiscordBot;

public class LoggerFormatter : ConsoleFormatter, IDisposable
{
    private readonly object _lock = new();
    private readonly IDisposable? _optionsReloadToken;
    private ConsoleFormatterOptions _options;

    public LoggerFormatter(IOptionsMonitor<ConsoleFormatterOptions> options) : base("logger")
    {
        _options = options.CurrentValue;
        _optionsReloadToken = options.OnChange(options => _options = options);
    }

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        lock (_lock)
        {
            if (logEntry.LogLevel == LogLevel.Trace)
            {
                textWriter.Write(GetForegroundColorEscapeCode(ConsoleColor.Gray));
            }

            DateTimeOffset dto = _options.UseUtcTimestamp ? DateTimeOffset.UtcNow : DateTimeOffset.Now;
            textWriter.Write($"[{dto.ToString(_options.TimestampFormat)}] ");
            if (_options.IncludeScopes)
            {
                textWriter.Write($"[{logEntry.Category}] ");
            }

            textWriter.Write(GetForegroundColorEscapeCode(logEntry.LogLevel switch
            {
                LogLevel.Trace => ConsoleColor.Gray,
                LogLevel.Debug => ConsoleColor.Green,
                LogLevel.Information => ConsoleColor.Magenta,
                LogLevel.Warning => ConsoleColor.Yellow,
                LogLevel.Error => ConsoleColor.Red,
                LogLevel.Critical => ConsoleColor.DarkRed,
                _ => throw new ArgumentException("Invalid log level specified.", nameof(logEntry))
            }));

            textWriter.Write
            (
                logEntry.LogLevel switch
                {
                    LogLevel.Trace => "[Trace] ",
                    LogLevel.Debug => "[Debug] ",
                    LogLevel.Information => "[Info]  ",
                    LogLevel.Warning => "[Warn]  ",
                    LogLevel.Error => "[Error] ",
                    LogLevel.Critical => "[Crit]  ",
                    _ => "This code path is unreachable."
                }
            );

            textWriter.Write(GetForegroundColorEscapeCode(ConsoleColor.White));
            textWriter.WriteLine(logEntry.State?.ToString());

            if (logEntry.Exception != null)
            {
                textWriter.WriteLine($"{logEntry.Exception} : {logEntry.Exception.Message}\n{logEntry.Exception.StackTrace}");
            }
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        _optionsReloadToken?.Dispose();
    }

    private static string GetForegroundColorEscapeCode(ConsoleColor color) =>
        color switch
        {
            ConsoleColor.Black => "\x1B[30m",
            ConsoleColor.DarkRed => "\x1B[31m",
            ConsoleColor.DarkGreen => "\x1B[32m",
            ConsoleColor.DarkYellow => "\x1B[33m",
            ConsoleColor.DarkBlue => "\x1B[34m",
            ConsoleColor.DarkMagenta => "\x1B[35m",
            ConsoleColor.DarkCyan => "\x1B[36m",
            ConsoleColor.Gray => "\x1B[37m",
            ConsoleColor.Red => "\x1B[1m\x1B[31m",
            ConsoleColor.Green => "\x1B[1m\x1B[32m",
            ConsoleColor.Yellow => "\x1B[1m\x1B[33m",
            ConsoleColor.Blue => "\x1B[1m\x1B[34m",
            ConsoleColor.Magenta => "\x1B[1m\x1B[35m",
            ConsoleColor.Cyan => "\x1B[1m\x1B[36m",
            ConsoleColor.White => "\x1B[1m\x1B[37m",
            _ => "\x1B[1m\x1B[37m"
        };
}