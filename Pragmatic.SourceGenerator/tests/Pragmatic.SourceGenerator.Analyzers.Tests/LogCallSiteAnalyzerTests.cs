using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Analyzers.Tests;

/// <summary>
///     PRAG2400–PRAG2410: what is wrong with a log call site, the guard on the alias that selects
///     Pragmatic's, and a masking attribute on a parameter nothing reads.
/// </summary>
public class LogCallSiteAnalyzerTests
{
    // Enough of both attributes, the logger and the masking attributes for the analyzer to key on.
    private const string Stubs = """
        namespace Microsoft.Extensions.Logging
        {
            public interface ILogger { }
            public interface ILogger<T> : ILogger { }
            public enum LogLevel { Trace, Debug, Information, Warning, Error, Critical, None }
            [System.AttributeUsage(System.AttributeTargets.Method)]
            public sealed class LoggerMessageAttribute : System.Attribute
            {
                public int EventId { get; set; } = -1;
                public LogLevel Level { get; set; } = LogLevel.None;
                public string Message { get; set; } = "";
            }
        }
        namespace Pragmatic.Logging.CallSites
        {
            [System.AttributeUsage(System.AttributeTargets.Method)]
            public sealed class LoggerMessageAttribute : System.Attribute
            {
                public int EventId { get; set; } = -1;
                public string? EventName { get; set; }
                public Microsoft.Extensions.Logging.LogLevel Level { get; set; } = Microsoft.Extensions.Logging.LogLevel.None;
                public string Message { get; set; } = "";
                public bool SkipEnabledCheck { get; set; }
            }
        }
        namespace Pragmatic
        {
            [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Parameter)]
            public sealed class NotLoggedAttribute : System.Attribute { }
        }
        namespace Pragmatic.Privacy
        {
            public enum DataCategory { Identity, Contact }
            [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Parameter)]
            public sealed class PersonalDataAttribute(DataCategory category) : System.Attribute
            {
                public DataCategory Category { get; } = category;
            }
        }
        """;

    private const string Alias = "global using LoggerMessageAttribute = global::Pragmatic.Logging.CallSites.LoggerMessageAttribute;\n";

    private static async Task<Diagnostic[]> RunAsync(string source, bool callSites = true, bool alias = true)
    {
        var trees = new List<SyntaxTree>
        {
            CSharpSyntaxTree.ParseText(Stubs, new CSharpParseOptions(LanguageVersion.Latest)),
            CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)),
        };
        if (alias)
            trees.Add(CSharpSyntaxTree.ParseText(Alias, new CSharpParseOptions(LanguageVersion.Latest)));

        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var compilation = CSharpCompilation.Create(
            "LogCallSiteTest", trees,
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")),
                MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "netstandard.dll")),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var properties = callSites
            ? new Dictionary<string, string> { ["PragmaticLogCallSites"] = "true" }
            : new Dictionary<string, string>();
        var options = new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty, new BuildPropertyOptions(properties));

        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new LogCallSiteAnalyzer()), options)
            .GetAnalyzerDiagnosticsAsync();

        return diagnostics.Where(d => d.Id.StartsWith("PRAG24", System.StringComparison.Ordinal)).ToArray();
    }

    private static string[] Ids(Diagnostic[] diagnostics) => diagnostics.Select(d => d.Id).OrderBy(i => i).ToArray();

    // ── The guard ───────────────────────────────────────────────────────────────────────────────────

    private const string AMicrosoftBoundCallSite = """
        using Microsoft.Extensions.Logging;
        namespace App;
        public static partial class Log
        {
            [LoggerMessage(Level = LogLevel.Information, Message = "Signed in {User}")]
            public static partial void SignedIn(ILogger logger, string user);
        }
        """;

    [Fact]
    public async Task WithoutTheAlias_ASimpleNameBoundToMicrosoftsAttributeIsAnError()
    {
        var diagnostics = await RunAsync(AMicrosoftBoundCallSite, alias: false);

        var guard = diagnostics.Should().ContainSingle(d => d.Id == "PRAG2408").Which;
        guard.Severity.Should().Be(DiagnosticSeverity.Error);
        guard.GetMessage().Should().Contain("SignedIn").And.Contain("Microsoft.Extensions.Logging.LoggerMessageAttribute");
    }

    /// <summary>The control: the same source with the alias binds to Pragmatic's and is valid.</summary>
    [Fact]
    public async Task WithTheAlias_TheSameSourceIsAPragmaticCallSite_AndNothingIsReported()
    {
        var diagnostics = await RunAsync(AMicrosoftBoundCallSite, alias: true);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task FullyQualified_MicrosoftsAttributeIsAnOptOut_NotReported()
    {
        var diagnostics = await RunAsync("""
            using Microsoft.Extensions.Logging;
            namespace App;
            public static partial class Log
            {
                [Microsoft.Extensions.Logging.LoggerMessage(Level = LogLevel.Information, Message = "Signed in {User}")]
                public static partial void SignedIn(ILogger logger, string user);
            }
            """, alias: false);

        Ids(diagnostics).Should().NotContain("PRAG2408");
    }

    [Fact]
    public async Task InAProjectThatDoesNotUseCallSites_TheGuardIsOff()
    {
        var diagnostics = await RunAsync(AMicrosoftBoundCallSite, callSites: false, alias: false);

        Ids(diagnostics).Should().NotContain("PRAG2408");
    }

    // ── The call site ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task APlaceholderWithoutAParameter_AndAParameterWithoutAPlaceholder()
    {
        var diagnostics = await RunAsync("""
            using Microsoft.Extensions.Logging;
            namespace App;
            public static partial class Log
            {
                [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} by {Customer}")]
                public static partial void Placed(ILogger logger, int orderId, string region);
            }
            """);

        Ids(diagnostics).Should().Equal("PRAG2401", "PRAG2402");
        diagnostics.Single(d => d.Id == "PRAG2401").GetMessage().Should().Contain("{Customer}");
        diagnostics.Single(d => d.Id == "PRAG2402").GetMessage().Should().Contain("region");
    }

    [Fact]
    public async Task NoLogger_NoLevel_AndANonPartialType()
    {
        var diagnostics = await RunAsync("""
            using Microsoft.Extensions.Logging;
            namespace App;
            public static partial class Log
            {
                [LoggerMessage(Message = "Started")]
                public static partial void Started();
            }
            public static class Plain
            {
                [LoggerMessage(Level = LogLevel.Information, Message = "Stopped")]
                public static partial void Stopped(ILogger logger);
            }
            """);

        Ids(diagnostics).Should().Contain("PRAG2403").And.Contain("PRAG2404").And.Contain("PRAG2407");
    }

    [Fact]
    public async Task AMalformedTemplate_AndAnAlignment()
    {
        var diagnostics = await RunAsync("""
            using Microsoft.Extensions.Logging;
            namespace App;
            public static partial class Log
            {
                [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId")]
                public static partial void Unclosed(ILogger logger, int orderId);

                [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId,8}")]
                public static partial void Aligned(ILogger logger, int orderId);
            }
            """);

        diagnostics.Where(d => d.Id == "PRAG2406").Should().HaveCount(2);
    }

    [Fact]
    public async Task AMethodThatIsNotPartialVoid()
    {
        var diagnostics = await RunAsync("""
            using Microsoft.Extensions.Logging;
            namespace App;
            public static partial class Log
            {
                [LoggerMessage(Level = LogLevel.Information, Message = "Count")]
                public static partial int Count(ILogger logger);
                public static partial int Count(ILogger logger) => 0;
            }
            """);

        Ids(diagnostics).Should().Contain("PRAG2400");
    }

    [Fact]
    public async Task TwoCallSitesOfATypeWithOneEventId()
    {
        var diagnostics = await RunAsync("""
            using Microsoft.Extensions.Logging;
            namespace App;
            public static partial class Log
            {
                [LoggerMessage(EventId = 7, Level = LogLevel.Information, Message = "Started")]
                public static partial void Started(ILogger logger);

                [LoggerMessage(EventId = 7, Level = LogLevel.Information, Message = "Stopped")]
                public static partial void Stopped(ILogger logger);
            }
            """);

        var duplicate = diagnostics.Should().ContainSingle(d => d.Id == "PRAG2405").Which;
        duplicate.GetMessage().Should().Contain("'Stopped' uses event id 7").And.Contain("'Started'");
    }

    // ── A masking attribute nothing reads ───────────────────────────────────────────────────────────

    [Fact]
    public async Task AMaskingAttributeOnAnOrdinaryParameterIsAnError()
    {
        var diagnostics = await RunAsync("""
            namespace App;
            public sealed class Accounts
            {
                public void SignIn([Pragmatic.NotLogged] string password, [Pragmatic.Privacy.PersonalData(Pragmatic.Privacy.DataCategory.Contact)] string email) { }
            }
            """);

        diagnostics.Where(d => d.Id == "PRAG2410").Should().HaveCount(2);
        diagnostics.Should().OnlyContain(d => d.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>
    ///     Before parameters were allowed this did not compile (CS0592), which forced
    ///     <c>[property: NotLogged]</c>; now it compiles and lands on the parameter, so it is an error here.
    /// </summary>
    [Fact]
    public async Task OnAPositionalRecordParameter_TheMessageSaysToTargetTheProperty()
    {
        var diagnostics = await RunAsync("""
            namespace App;
            public sealed record Login([Pragmatic.NotLogged] string Password);
            """);

        diagnostics.Should().ContainSingle(d => d.Id == "PRAG2410")
            .Which.GetMessage().Should().Contain("[property: NotLogged]");
    }

    [Fact]
    public async Task OnACallSiteParameter_AMaskingAttributeIsWhatItIsFor()
    {
        var diagnostics = await RunAsync("""
            using Microsoft.Extensions.Logging;
            namespace App;
            public static partial class Log
            {
                [LoggerMessage(Level = LogLevel.Information, Message = "Receipt sent to {Email}")]
                public static partial void ReceiptSent(ILogger logger, [Pragmatic.Privacy.PersonalData(Pragmatic.Privacy.DataCategory.Contact)] string email);
            }
            """);

        diagnostics.Should().BeEmpty();
    }
}
