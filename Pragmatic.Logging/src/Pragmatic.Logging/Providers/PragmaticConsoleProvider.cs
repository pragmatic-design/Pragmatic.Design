using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Logging.Providers;

/// <summary>
/// Advanced console logger provider with configurable color palette, automatic no-color fallback,
/// and ANSI escape sequence detection. Provides rich formatting with performance optimization.
/// </summary>
/// <remarks>
/// <para>
/// This provider automatically detects console capabilities and adapts output accordingly:
/// - ANSI color support in modern terminals (Linux, macOS, Windows Terminal)
/// - Windows Console API colors for legacy Windows consoles
/// - Plain text fallback when colors are not supported
/// - Structured data formatting with syntax highlighting
/// </para>
/// <para>
/// Performance features:
/// - Thread-safe writing with minimal lock contention
/// - Shared StringBuilder with capacity management
/// - Performance metrics tracking for monitoring
/// - Optimized color application algorithms
/// </para>
/// </remarks>
/// <example>
/// Basic usage in application:
/// <code>
/// services.Configure&lt;PragmaticLoggingOptions&gt;(options =>
/// {
///     options.Providers.Console.Enabled = true;
///     options.Providers.Console.UseColors = true;
///     options.Providers.Console.IncludeStructuredProperties = true;
///     options.Providers.Console.TimestampFormat = "HH:mm:ss.fff";
/// });
/// 
/// // Example output with colors:
/// // [14:32:15.123] [INFO] UserService: User john.doe logged in successfully {UserId: "john.doe", IPAddress: "192.168.1.1"}
/// </code>
/// 
/// Custom color scheme configuration:
/// <code>
/// var customColors = new PragmaticConsoleProvider.ColorScheme
/// {
///     InformationColor = ConsoleColor.Green,
///     WarningColor = ConsoleColor.DarkYellow,
///     ErrorColor = ConsoleColor.DarkRed,
///     TimestampColor = ConsoleColor.DarkBlue
/// };
/// 
/// var provider = new PragmaticConsoleProvider(customColors);
/// </code>
/// 
/// Environment detection example:
/// <code>
/// // Provider automatically detects:
/// // - Windows Terminal: Uses ANSI colors
/// // - Legacy cmd.exe: Uses Console.ForegroundColor
/// // - CI/CD environments: Disables colors automatically
/// // - Docker containers: Detects TTY support
/// </code>
/// </example>
public sealed class PragmaticConsoleProvider : PragmaticLoggerProviderBase
{
    private readonly object _writeLock = new();
    private readonly StringBuilder _stringBuilder = new(1024);
    private readonly ColorScheme _colorScheme;
    private readonly bool _supportsAnsiColors;
    private readonly bool _supportsConsoleColors;

    // Performance tracking
    private readonly Stopwatch _performanceStopwatch = Stopwatch.StartNew();
    private long _totalWrites;
    private double _averageWriteLatency;

    /// <summary>
    /// Color scheme configuration for different log levels and components.
    /// </summary>
    public class ColorScheme
    {
        public ConsoleColor TraceColor { get; set; } = ConsoleColor.DarkGray;
        public ConsoleColor DebugColor { get; set; } = ConsoleColor.Gray;
        public ConsoleColor InformationColor { get; set; } = ConsoleColor.White;
        public ConsoleColor WarningColor { get; set; } = ConsoleColor.Yellow;
        public ConsoleColor ErrorColor { get; set; } = ConsoleColor.Red;
        public ConsoleColor CriticalColor { get; set; } = ConsoleColor.Magenta;

        public ConsoleColor TimestampColor { get; set; } = ConsoleColor.DarkGreen;
        public ConsoleColor CategoryColor { get; set; } = ConsoleColor.DarkCyan;
        public ConsoleColor StructuredDataColor { get; set; } = ConsoleColor.DarkBlue;
        public ConsoleColor ExceptionColor { get; set; } = ConsoleColor.DarkRed;

        // ANSI color codes for terminals that support them
        public string AnsiTraceColor { get; set; } = "\x1b[90m"; // Dark gray
        public string AnsiDebugColor { get; set; } = "\x1b[37m"; // Light gray
        public string AnsiInformationColor { get; set; } = "\x1b[97m"; // White
        public string AnsiWarningColor { get; set; } = "\x1b[93m"; // Yellow
        public string AnsiErrorColor { get; set; } = "\x1b[91m"; // Red
        public string AnsiCriticalColor { get; set; } = "\x1b[95m"; // Magenta

        public string AnsiTimestampColor { get; set; } = "\x1b[32m"; // Green
        public string AnsiCategoryColor { get; set; } = "\x1b[36m"; // Cyan
        public string AnsiStructuredDataColor { get; set; } = "\x1b[34m"; // Blue
        public string AnsiExceptionColor { get; set; } = "\x1b[31m"; // Red
        public string AnsiReset { get; set; } = "\x1b[0m"; // Reset

        public static ColorScheme Default => new();

        public static ColorScheme HighContrast => new()
        {
            TraceColor = ConsoleColor.Gray,
            DebugColor = ConsoleColor.White,
            InformationColor = ConsoleColor.Green,
            WarningColor = ConsoleColor.Yellow,
            ErrorColor = ConsoleColor.Red,
            CriticalColor = ConsoleColor.Magenta,
            TimestampColor = ConsoleColor.Cyan,
            CategoryColor = ConsoleColor.Blue
        };

        public static ColorScheme Monochrome => new()
        {
            TraceColor = ConsoleColor.White,
            DebugColor = ConsoleColor.White,
            InformationColor = ConsoleColor.White,
            WarningColor = ConsoleColor.White,
            ErrorColor = ConsoleColor.White,
            CriticalColor = ConsoleColor.White,
            TimestampColor = ConsoleColor.White,
            CategoryColor = ConsoleColor.White,
            StructuredDataColor = ConsoleColor.White,
            ExceptionColor = ConsoleColor.White
        };
    }

    /// <summary>
    /// Initializes a new instance of PragmaticConsoleProvider.
    /// </summary>
    /// <param name="name">Provider name</param>
    /// <param name="configuration">Provider configuration</param>
    public PragmaticConsoleProvider(string name, IPragmaticProviderConfiguration configuration)
        : base(name, configuration)
    {
        ValidateConsoleConfiguration(configuration);

        // Initialize color support detection
        _supportsConsoleColors = DetectConsoleColorSupport();
        _supportsAnsiColors = DetectAnsiColorSupport();

        // Initialize color scheme
        _colorScheme = GetColorScheme();
    }

    /// <inheritdoc />
    protected override void WriteLogCore(LogEntry logEntry)
    {
        var writeStart = Stopwatch.GetTimestamp();

        lock (_writeLock)
        {
            try
            {
                var formattedMessage = FormatLogEntry(logEntry);
                WriteToConsole(logEntry.LogLevel, formattedMessage, logEntry);

                // Update performance metrics
                var writeTime = Stopwatch.GetElapsedTime(writeStart).TotalMilliseconds;
                _averageWriteLatency = (_averageWriteLatency * _totalWrites + writeTime) / (_totalWrites + 1);
                Interlocked.Increment(ref _totalWrites);
            }
            catch (Exception)
            {
                // Fail silently to prevent logging from breaking the application
            }
        }
    }

    /// <inheritdoc />
    protected override ProviderHealthStatus PerformCustomHealthCheck()
    {
        try
        {
            // Test console availability and color support
            if (_supportsConsoleColors)
            {
                var originalColor = Console.ForegroundColor;
                Console.ForegroundColor = originalColor;
            }

            return ProviderHealthStatus.Healthy;
        }
        catch (Exception)
        {
            return ProviderHealthStatus.Degraded;
        }
    }

    /// <inheritdoc />
    protected override Dictionary<string, object?> GetCustomMetrics()
    {
        var metrics = base.GetCustomMetrics();

        var elapsed = _performanceStopwatch.Elapsed.TotalSeconds;
        var throughput = elapsed > 0 ? _totalWrites / elapsed : 0;

        metrics["SupportsColors"] = _supportsConsoleColors;
        metrics["SupportsAnsiColors"] = _supportsAnsiColors;
        metrics["ConsoleWidth"] = GetConsoleWidth();
        metrics["ConsoleHeight"] = GetConsoleHeight();
        metrics["OutputEncoding"] = Console.OutputEncoding.EncodingName;
        metrics["IsOutputRedirected"] = Console.IsOutputRedirected;
        metrics["IsErrorRedirected"] = Console.IsErrorRedirected;
        metrics["IsInputRedirected"] = Console.IsInputRedirected;
        metrics["TotalWrites"] = _totalWrites;
        metrics["AverageWriteLatency"] = _averageWriteLatency;
        metrics["WriteThroughput"] = throughput;
        metrics["ColorScheme"] = GetCustomProperty<string>("ColorScheme", "Default");
        metrics["TerminalType"] = Environment.GetEnvironmentVariable("TERM") ?? "unknown";

        return metrics;
    }

    private string FormatLogEntry(LogEntry logEntry)
    {
        _stringBuilder.Clear();

        var config = Configuration.Formatting;
        var template = config.MessageTemplate;
        var useRichFormatting = GetCustomProperty<bool>("UseRichFormatting", true);

        if (useRichFormatting && ShouldUseColors())
        {
            return FormatWithColors(logEntry, template, config);
        }
        else
        {
            return FormatPlain(logEntry, template, config);
        }
    }

    private string FormatWithColors(LogEntry logEntry, string template, FormattingConfiguration config)
    {
        var parts = new List<(string text, ConsoleColor? color, string? ansiColor)>();

        // Parse template and apply colors to different parts
        var formatted = template;

        // Timestamp
        var timestamp = logEntry.FormatTimestamp(config.TimestampFormat, config.UseUtcTimestamp);
        if (_supportsAnsiColors)
        {
            formatted = formatted.Replace("{Timestamp}", $"{_colorScheme.AnsiTimestampColor}{timestamp}{_colorScheme.AnsiReset}");
        }
        else
        {
            formatted = formatted.Replace("{Timestamp}", timestamp);
            parts.Add((timestamp, _colorScheme.TimestampColor, null));
        }

        // Level
        var level = GetLevelDisplayName(logEntry.LogLevel);
        var levelColor = GetLogLevelColor(logEntry.LogLevel);
        var levelAnsiColor = GetLogLevelAnsiColor(logEntry.LogLevel);

        if (_supportsAnsiColors)
        {
            formatted = formatted.Replace("{Level}", $"{levelAnsiColor}{level}{_colorScheme.AnsiReset}");
        }
        else
        {
            formatted = formatted.Replace("{Level}", level);
            parts.Add((level, levelColor, null));
        }

        // Category
        var category = TruncateCategory(logEntry.Category);
        if (_supportsAnsiColors)
        {
            formatted = formatted.Replace("{Category}", $"{_colorScheme.AnsiCategoryColor}{category}{_colorScheme.AnsiReset}");
        }
        else
        {
            formatted = formatted.Replace("{Category}", category);
            parts.Add((category, _colorScheme.CategoryColor, null));
        }

        // Message
        var message = FormatMessage(logEntry);
        formatted = formatted.Replace("{Message}", message);

        // Add structured properties if enabled
        if (Configuration.IncludeStructuredProperties && logEntry.Properties.Count > 0)
        {
            _stringBuilder.Append(formatted);
            AppendStructuredPropertiesWithColors(logEntry);
            formatted = _stringBuilder.ToString();
            _stringBuilder.Clear();
        }

        // Add exception details if present
        if (logEntry.Exception != null && config.IncludeExceptionDetails)
        {
            _stringBuilder.Append(formatted);
            _stringBuilder.AppendLine();
            AppendExceptionWithColors(logEntry.Exception);
            formatted = _stringBuilder.ToString();
            _stringBuilder.Clear();
        }

        return formatted;
    }

    private string FormatPlain(LogEntry logEntry, string template, FormattingConfiguration config)
    {
        var formatted = template
            .Replace("{Timestamp}", logEntry.FormatTimestamp(config.TimestampFormat, config.UseUtcTimestamp))
            .Replace("{Level}", GetLevelDisplayName(logEntry.LogLevel))
            .Replace("{Category}", TruncateCategory(logEntry.Category))
            .Replace("{Message}", FormatMessage(logEntry));

        // Add structured properties if enabled
        if (Configuration.IncludeStructuredProperties && logEntry.Properties.Count > 0)
        {
            _stringBuilder.Append(formatted);
            AppendStructuredProperties(logEntry);
            formatted = _stringBuilder.ToString();
            _stringBuilder.Clear();
        }

        // Add exception details if present
        if (logEntry.Exception != null && config.IncludeExceptionDetails)
        {
            _stringBuilder.Append(formatted);
            _stringBuilder.AppendLine();
            AppendException(logEntry.Exception);
            formatted = _stringBuilder.ToString();
            _stringBuilder.Clear();
        }

        return formatted;
    }

    private void AppendStructuredPropertiesWithColors(LogEntry logEntry)
    {
        if (logEntry.Properties.Count == 0)
            return;

        if (_supportsAnsiColors)
        {
            _stringBuilder.Append(CultureInfo.InvariantCulture, $" {_colorScheme.AnsiStructuredDataColor}[");
        }
        else
        {
            _stringBuilder.Append(" [");
        }

        var first = true;

        foreach (var kvp in logEntry.Properties)
        {
            if (!first)
                _stringBuilder.Append(", ");

            _stringBuilder.Append(kvp.Key);
            _stringBuilder.Append('=');

            if (kvp.Value == null)
            {
                _stringBuilder.Append("null");
            }
            else if (kvp.Value is string stringValue)
            {
                _stringBuilder.Append('"').Append(stringValue).Append('"');
            }
            else
            {
                _stringBuilder.Append(kvp.Value);
            }

            first = false;
        }

        if (_supportsAnsiColors)
        {
            _stringBuilder.Append(CultureInfo.InvariantCulture, $"]{_colorScheme.AnsiReset}");
        }
        else
        {
            _stringBuilder.Append(']');
        }
    }

    private void AppendExceptionWithColors(Exception exception)
    {
        if (_supportsAnsiColors)
        {
            _stringBuilder.Append(CultureInfo.InvariantCulture, $"{_colorScheme.AnsiExceptionColor}Exception: {exception.GetType().FullName}");
            _stringBuilder.AppendLine();
            _stringBuilder.Append(CultureInfo.InvariantCulture, $"Message: {exception.Message}{_colorScheme.AnsiReset}");
        }
        else
        {
            _stringBuilder.Append("Exception: ");
            _stringBuilder.AppendLine(exception.GetType().FullName);
            _stringBuilder.Append("Message: ");
            _stringBuilder.AppendLine(exception.Message);
        }

        if (!string.IsNullOrEmpty(exception.StackTrace))
        {
            _stringBuilder.AppendLine();
            _stringBuilder.AppendLine("Stack Trace:");
            _stringBuilder.AppendLine(exception.StackTrace);
        }

        // Handle inner exceptions
        var innerException = exception.InnerException;
        var depth = 1;
        while (innerException != null && depth <= 3)
        {
            _stringBuilder.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"--- Inner Exception {depth} ---");
            _stringBuilder.Append("Type: ");
            _stringBuilder.AppendLine(innerException.GetType().FullName);
            _stringBuilder.Append("Message: ");
            _stringBuilder.AppendLine(innerException.Message);

            innerException = innerException.InnerException;
            depth++;
        }
    }

    private void AppendStructuredProperties(LogEntry logEntry)
    {
        if (logEntry.Properties.Count == 0)
            return;

        _stringBuilder.Append(" [");
        var first = true;

        foreach (var kvp in logEntry.Properties)
        {
            if (!first)
                _stringBuilder.Append(", ");

            _stringBuilder.Append(kvp.Key);
            _stringBuilder.Append('=');

            if (kvp.Value == null)
            {
                _stringBuilder.Append("null");
            }
            else if (kvp.Value is string stringValue)
            {
                _stringBuilder.Append('"').Append(stringValue).Append('"');
            }
            else
            {
                _stringBuilder.Append(kvp.Value);
            }

            first = false;
        }

        _stringBuilder.Append(']');
    }

    private void AppendException(Exception exception)
    {
        _stringBuilder.Append("Exception: ");
        _stringBuilder.AppendLine(exception.GetType().FullName);
        _stringBuilder.Append("Message: ");
        _stringBuilder.AppendLine(exception.Message);

        if (!string.IsNullOrEmpty(exception.StackTrace))
        {
            _stringBuilder.AppendLine("Stack Trace:");
            _stringBuilder.AppendLine(exception.StackTrace);
        }

        var innerException = exception.InnerException;
        var depth = 1;
        while (innerException != null && depth <= 3)
        {
            _stringBuilder.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"--- Inner Exception {depth} ---");
            _stringBuilder.Append("Type: ");
            _stringBuilder.AppendLine(innerException.GetType().FullName);
            _stringBuilder.Append("Message: ");
            _stringBuilder.AppendLine(innerException.Message);

            innerException = innerException.InnerException;
            depth++;
        }
    }

    private string FormatMessage(LogEntry logEntry)
    {
        var message = logEntry.Message;

        var customFormatters = Configuration.Formatting.CustomFormatters;
        if (customFormatters.Count > 0 && logEntry.Properties.Count > 0)
        {
            foreach (var kvp in logEntry.Properties)
            {
                if (kvp.Value != null && customFormatters.TryGetValue(kvp.Value.GetType(), out var formatter))
                {
                    var placeholder = $"{{{kvp.Key}}}";
                    if (message.Contains(placeholder))
                    {
                        message = message.Replace(placeholder, formatter(kvp.Value));
                    }
                }
            }
        }

        return message;
    }

    private void WriteToConsole(LogLevel logLevel, string message, LogEntry logEntry)
    {
        var useStdErrorForErrors = GetCustomProperty<bool>("UseStdErrorForErrors", false);
        var adaptToConsoleWidth = GetCustomProperty<bool>("AdaptToConsoleWidth", true);

        // Determine output stream
        var useStdError = useStdErrorForErrors && (logLevel >= LogLevel.Error);
        var outputStream = useStdError ? Console.Error : Console.Out;

        // Adapt message to console width if enabled
        if (adaptToConsoleWidth && !Console.IsOutputRedirected)
        {
            message = AdaptMessageToConsoleWidth(message);
        }

        if (ShouldUseColors() && !_supportsAnsiColors && _supportsConsoleColors)
        {
            WriteWithConsoleColors(logLevel, message, outputStream);
        }
        else
        {
            // ANSI colors are already embedded in the message, or no colors needed
            outputStream.WriteLine(message);
        }
    }

    private void WriteWithConsoleColors(LogLevel logLevel, string message, TextWriter outputStream)
    {
        var originalColor = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = GetLogLevelColor(logLevel);
            outputStream.WriteLine(message);
        }
        finally
        {
            Console.ForegroundColor = originalColor;
        }
    }

    private bool ShouldUseColors()
    {
        var forceColors = GetCustomProperty<bool>("ForceColors", false);
        if (forceColors)
            return true;

        var noColor = Environment.GetEnvironmentVariable("NO_COLOR");
        if (!string.IsNullOrEmpty(noColor))
            return false;

        var useColors = GetCustomProperty<bool>("UseColors", true);
        if (!useColors)
            return false;

        return (_supportsConsoleColors || _supportsAnsiColors) && !Console.IsOutputRedirected;
    }

    private bool DetectConsoleColorSupport()
    {
        try
        {
            if (Console.IsOutputRedirected || !Environment.UserInteractive)
                return false;

            // Test console color capability
            var original = Console.ForegroundColor;
            Console.ForegroundColor = original;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool DetectAnsiColorSupport()
    {
        // Check environment variables that indicate ANSI support
        var term = Environment.GetEnvironmentVariable("TERM");
        var colorTerm = Environment.GetEnvironmentVariable("COLORTERM");

        if (!string.IsNullOrEmpty(colorTerm))
            return true;

        if (!string.IsNullOrEmpty(term))
        {
            var termLower = term.ToLowerInvariant();
            if (termLower.Contains("color") || termLower.Contains("ansi") ||
                termLower.Contains("xterm") || termLower.Contains("screen"))
                return true;
        }

        // Check for Windows Terminal, VS Code terminal, etc.
        var wtSession = Environment.GetEnvironmentVariable("WT_SESSION");
        var vscode = Environment.GetEnvironmentVariable("VSCODE_PID");

        if (!string.IsNullOrEmpty(wtSession) || !string.IsNullOrEmpty(vscode))
            return true;

        // On Windows, check if we're in a modern terminal
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var conEmuANSI = Environment.GetEnvironmentVariable("ConEmuANSI");
            var ansicon = Environment.GetEnvironmentVariable("ANSICON");

            if (conEmuANSI == "ON" || !string.IsNullOrEmpty(ansicon))
                return true;
        }

        return false;
    }

    private ColorScheme GetColorScheme()
    {
        var schemeName = GetCustomProperty<string>("ColorScheme", "Default");
        return schemeName.ToUpperInvariant() switch
        {
            "HIGHCONTRAST" => ColorScheme.HighContrast,
            "MONOCHROME" => ColorScheme.Monochrome,
            "DEFAULT" => ColorScheme.Default,
            _ => ColorScheme.Default
        };
    }

    private ConsoleColor GetLogLevelColor(LogLevel logLevel)
    {
        return logLevel switch
        {
            LogLevel.Trace => _colorScheme.TraceColor,
            LogLevel.Debug => _colorScheme.DebugColor,
            LogLevel.Information => _colorScheme.InformationColor,
            LogLevel.Warning => _colorScheme.WarningColor,
            LogLevel.Error => _colorScheme.ErrorColor,
            LogLevel.Critical => _colorScheme.CriticalColor,
            _ => ConsoleColor.White
        };
    }

    private string GetLogLevelAnsiColor(LogLevel logLevel)
    {
        return logLevel switch
        {
            LogLevel.Trace => _colorScheme.AnsiTraceColor,
            LogLevel.Debug => _colorScheme.AnsiDebugColor,
            LogLevel.Information => _colorScheme.AnsiInformationColor,
            LogLevel.Warning => _colorScheme.AnsiWarningColor,
            LogLevel.Error => _colorScheme.AnsiErrorColor,
            LogLevel.Critical => _colorScheme.AnsiCriticalColor,
            _ => _colorScheme.AnsiInformationColor
        };
    }

    private static string GetLevelDisplayName(LogLevel logLevel)
    {
        return logLevel switch
        {
            LogLevel.Trace => "TRCE",
            LogLevel.Debug => "DBUG",
            LogLevel.Information => "INFO",
            LogLevel.Warning => "WARN",
            LogLevel.Error => "FAIL",
            LogLevel.Critical => "CRIT",
            _ => logLevel.ToString().ToUpperInvariant()
        };
    }

    private string TruncateCategory(string category)
    {
        var truncateCategories = GetCustomProperty<bool>("TruncateCategories", true);
        if (!truncateCategories)
            return category;

        var maxLength = GetCustomProperty<int>("MaxCategoryLength", 40);
        if (category.Length <= maxLength)
            return category;

        var lastDotIndex = category.LastIndexOf('.');
        if (lastDotIndex > 0 && category.Length - lastDotIndex < maxLength)
        {
            var namespacePart = category.AsSpan(0, lastDotIndex);
            var classPart = category.AsSpan(lastDotIndex);

            var availableSpace = maxLength - classPart.Length - 3;
            if (availableSpace > 0)
            {
                var truncatedNamespace = namespacePart.Slice(0, Math.Min(availableSpace, namespacePart.Length));
                return string.Concat(truncatedNamespace, "...".AsSpan(), classPart);
            }
        }

        return string.Concat(category.AsSpan(0, maxLength - 3), "...".AsSpan());
    }

    private string AdaptMessageToConsoleWidth(string message)
    {
        var consoleWidth = GetConsoleWidth();
        if (consoleWidth <= 0 || message.Length <= consoleWidth)
            return message;

        var adaptationMode = GetCustomProperty<string>("WidthAdaptationMode", "Wrap");

        return adaptationMode.ToUpperInvariant() switch
        {
            "TRUNCATE" => TruncateMessage(message, consoleWidth),
            "WRAP" => WrapMessage(message, consoleWidth),
            _ => message
        };
    }

    private static string TruncateMessage(string message, int maxWidth)
    {
        if (message.Length <= maxWidth)
            return message;

        return string.Concat(message.AsSpan(0, maxWidth - 3), "...".AsSpan());
    }

    private static string WrapMessage(string message, int maxWidth)
    {
        if (message.Length <= maxWidth)
            return message;

        var sb = new StringBuilder(message.Length + (message.Length / maxWidth) * Environment.NewLine.Length);
        var lines = message.Split(new[] { Environment.NewLine }, StringSplitOptions.None);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            while (line.Length > maxWidth)
            {
                sb.AppendLine(line.AsSpan(0, maxWidth).ToString());
                line = string.Concat("  ", line.AsSpan(maxWidth)); // Indent continuation lines
            }

            if (i < lines.Length - 1)
                sb.AppendLine(line);
            else
                sb.Append(line);
        }

        return sb.ToString();
    }

    private static int GetConsoleWidth()
    {
        try
        {
            return Console.IsOutputRedirected ? 80 : Console.WindowWidth;
        }
        catch (Exception)
        {
            return 80;
        }
    }

    private static int GetConsoleHeight()
    {
        try
        {
            return Console.IsOutputRedirected ? 24 : Console.WindowHeight;
        }
        catch (Exception)
        {
            return 24;
        }
    }

    private T GetCustomProperty<T>(string key, T defaultValue)
    {
        if (Configuration.CustomProperties.TryGetValue(key, out var value) && value is T typedValue)
        {
            return typedValue;
        }
        return defaultValue;
    }

    private static void ValidateConsoleConfiguration(IPragmaticProviderConfiguration configuration)
    {
        if (configuration.Performance.EnableBatching)
        {
            throw new ArgumentException(
                "Console provider should not use batching — it introduces latency for interactive output. " +
                "Disable batching or use a file/JSON provider for high-throughput logging.");
        }

        if (string.IsNullOrWhiteSpace(configuration.Formatting.MessageTemplate))
        {
            throw new ArgumentException("Console provider requires a message template");
        }
    }
}

/// <summary>
/// Enhanced console-specific configuration options and factory methods.
/// </summary>
public static class PragmaticConsoleConfiguration
{
    /// <summary>
    /// Creates a console configuration with advanced console features.
    /// </summary>
    /// <returns>Advanced console configuration</returns>
    public static PragmaticProviderConfiguration ForAdvancedConsole()
    {
        return new PragmaticProviderConfiguration
        {
            MinimumLevel = LogLevel.Information,
            IncludeStructuredProperties = true,
            IncludeContextEnrichment = true,
            Formatting = new FormattingConfiguration
            {
                TimestampFormat = "HH:mm:ss.fff",
                UseUtcTimestamp = false,
                MessageTemplate = "[{Timestamp}] [{Level}] {Category}: {Message}",
                IncludeExceptionDetails = true,
                MaxMessageLength = 0,
                PrettyPrintJson = false
            },
            Performance = new PerformanceConfiguration
            {
                EnableBatching = false,
                UseZeroAllocation = true
            },
            ContextFilter = new ContextFilterConfiguration
            {
                Mode = ContextFilterMode.Exclude,
                PropertyNames = new HashSet<string> { "MachineName", "ProcessId" }
            },
            CustomProperties = new Dictionary<string, object?>
            {
                ["UseColors"] = true,
                ["ForceColors"] = false,
                ["ColorScheme"] = "Default", // Default, HighContrast, Monochrome
                ["UseRichFormatting"] = true,
                ["TruncateCategories"] = true,
                ["MaxCategoryLength"] = 40,
                ["UseStdErrorForErrors"] = false,
                ["AdaptToConsoleWidth"] = true,
                ["WidthAdaptationMode"] = "Wrap" // Wrap, Truncate
            }
        };
    }

    /// <summary>
    /// Creates a console configuration optimized for CI/CD environments.
    /// </summary>
    /// <returns>CI/CD optimized console configuration</returns>
    public static PragmaticProviderConfiguration ForCiCd()
    {
        var config = ForAdvancedConsole();

        // CI/CD optimizations
        config.CustomProperties["UseColors"] = false; // Most CI systems don't support colors well
        config.CustomProperties["UseRichFormatting"] = false;
        config.CustomProperties["AdaptToConsoleWidth"] = false;
        config.CustomProperties["TruncateCategories"] = false; // Full category names for debugging
        config.Formatting.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff"; // Full timestamp for CI logs

        return config;
    }

    /// <summary>
    /// Creates a console configuration optimized for development environments.
    /// </summary>
    /// <returns>Development-optimized console configuration</returns>
    public static PragmaticProviderConfiguration ForDevelopment()
    {
        var config = ForAdvancedConsole();

        // Development optimizations
        config.MinimumLevel = LogLevel.Debug;
        config.CustomProperties["UseColors"] = true;
        config.CustomProperties["ColorScheme"] = "HighContrast";
        config.CustomProperties["UseRichFormatting"] = true;
        config.CustomProperties["AdaptToConsoleWidth"] = true;
        config.CustomProperties["WidthAdaptationMode"] = "Wrap";

        return config;
    }

    /// <summary>
    /// Creates a console configuration for high-contrast accessibility.
    /// </summary>
    /// <returns>High-contrast console configuration</returns>
    public static PragmaticProviderConfiguration ForHighContrast()
    {
        var config = ForAdvancedConsole();

        config.CustomProperties["ColorScheme"] = "HighContrast";
        config.CustomProperties["UseRichFormatting"] = true;

        return config;
    }

    /// <summary>
    /// Creates a console configuration with no colors (monochrome).
    /// </summary>
    /// <returns>Monochrome console configuration</returns>
    public static PragmaticProviderConfiguration ForMonochrome()
    {
        var config = ForAdvancedConsole();

        config.CustomProperties["UseColors"] = false;
        config.CustomProperties["ColorScheme"] = "Monochrome";
        config.CustomProperties["UseRichFormatting"] = false;

        return config;
    }
}
