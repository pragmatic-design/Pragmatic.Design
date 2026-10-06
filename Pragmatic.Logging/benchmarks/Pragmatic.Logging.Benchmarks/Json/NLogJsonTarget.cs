using NLog;
using NLog.Layouts;
using NLog.Targets;

namespace Pragmatic.Logging.Benchmarks.Json;

/// <summary>NLog's own JSON layout, rendered for each event.</summary>
internal sealed class NLogJsonTarget : TargetWithLayout
{
    public NLogJsonTarget()
    {
        Name = "json";
        Layout = new JsonLayout
        {
            Attributes =
            {
                new JsonAttribute("time", "${date:format=o}"),
                new JsonAttribute("level", "${level}"),
                new JsonAttribute("logger", "${logger}"),
                new JsonAttribute("message", "${message}"),
            },
            IncludeEventProperties = true,
        };
    }

    /// <summary>The last line written; for the equivalence check only.</summary>
    public string LastLine { get; private set; } = "";

    protected override void Write(LogEventInfo logEvent) => LastLine = RenderLogEvent(Layout, logEvent);
}
