using System.IO;
using System.Text;

namespace QuestMultiStream.App;

internal static class DesktopAppLog
{
    private static readonly object Sync = new();
    private static string? _logFilePath;

    public static string Initialize()
    {
        lock (Sync)
        {
            if (!string.IsNullOrWhiteSpace(_logFilePath))
            {
                return _logFilePath;
            }

            var logRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "QuestMultiStream",
                "logs");
            Directory.CreateDirectory(logRoot);

            _logFilePath = Path.Combine(logRoot, $"app-{DateTime.Now:yyyyMMdd}.log");
            WriteCore("INFO", "App logging initialized.");
            WriteCore("INFO", $"Base directory: {AppContext.BaseDirectory}");
            WriteCore("INFO", $"Command line: {Environment.CommandLine}");
            return _logFilePath;
        }
    }

    public static void Info(string message)
        => Write("INFO", message);

    public static void Error(string message, Exception? exception = null)
    {
        var builder = new StringBuilder(message);
        if (exception is not null)
        {
            builder.AppendLine();
            builder.AppendLine(exception.ToString());
        }

        Write("ERROR", builder.ToString());
    }

    private static void Write(string level, string message)
    {
        lock (Sync)
        {
            Initialize();
            WriteCore(level, message);
        }
    }

    private static void WriteCore(string level, string message)
    {
        if (string.IsNullOrWhiteSpace(_logFilePath))
        {
            return;
        }

        try
        {
            File.AppendAllText(
                _logFilePath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}",
                Encoding.UTF8);
        }
        catch
        {
        }
    }
}
