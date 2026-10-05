using Pragmatic.Logging.Context.Providers;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Logging.Tests.Context;

/// <summary>
///     Process context enriches an entry with what identifies the process, and not with its command line.
/// </summary>
/// <remarks>
///     <para>
///         A command line is where secrets travel: a connection string, a token, a password passed as an
///         argument or by a launcher. Written into every entry, it copies them into every sink, and
///         declared redaction cannot help, because nobody declared anything about a string read from the
///         environment.
///     </para>
///     <para>
///         Against the provider itself rather than through a preset's output. The ambient
///         <c>ContextManager.Instance</c> is process-wide, and tests that configure enrichment with
///         process context off unregister this provider from it, so an entry's properties depend on which
///         tests ran first. Worse, "no <c>CommandLine</c> in the entry" would then pass because the whole
///         provider was gone.
///     </para>
/// </remarks>
public class ProcessContextLeavesTheCommandLineOutTests
{
    [Fact]
    public void ProcessContext_HasNoCommandLine()
    {
        new ProcessContextProvider().GetContextProperties().Should().NotContainKey("CommandLine");
    }

    /// <summary>
    ///     The control: the rest of the process identity is still there, so the property is gone because it
    ///     was removed, not because process context stopped.
    /// </summary>
    [Fact]
    public void ProcessContext_StillIdentifiesTheProcess()
    {
        var properties = new ProcessContextProvider().GetContextProperties();

        properties.Should().ContainKey("ProcessId")
            .And.ContainKey("ProcessName")
            .And.ContainKey("ApplicationName")
            .And.ContainKey("WorkingDirectory");
    }
}
