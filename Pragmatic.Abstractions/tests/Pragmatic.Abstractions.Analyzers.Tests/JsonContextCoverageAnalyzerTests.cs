using Microsoft.CodeAnalysis.Testing;
using Xunit;
using AnalyzerVerifier = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Pragmatic.Abstractions.Analyzers.JsonContextCoverageAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Pragmatic.Abstractions.Analyzers.Tests;

/// <summary>
///     Tests for <see cref="JsonContextCoverageAnalyzer"/> (PRAG2800). Inline stubs reproduce the
///     Pragmatic marker interfaces and the System.Text.Json context types the analyzer keys off
///     (they are not in the test's default reference assemblies).
/// </summary>
public class JsonContextCoverageAnalyzerTests
{
    // Usings first, then the stub namespaces (a using may reference a namespace declared later in
    // the same file), then the test body.
    private const string Usings = @"using Pragmatic.Messaging;
using Pragmatic.Jobs;
using System.Text.Json.Serialization;
";

    private const string Stub = @"
namespace Pragmatic.Messaging { public interface IMessageHandler<T> { } }
namespace Pragmatic.Jobs { public interface IJob<T> { } }
namespace System.Text.Json.Serialization
{
    [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
    public sealed class JsonSerializableAttribute : System.Attribute { public JsonSerializableAttribute(System.Type type) { } }
    public abstract class JsonSerializerContext { }
}
";

    private static string Source(string body) => Usings + Stub + body;

    private static DiagnosticResult Expect() => AnalyzerVerifier.Diagnostic("PRAG2800");

    [Fact]
    public async Task Handler_PayloadNotInContext_ReportsDiagnostic()
    {
        var test = Source(@"
public class OrderPlaced { }
public class Other { }

[JsonSerializable(typeof(Other))]
public partial class AppJsonContext : JsonSerializerContext { }

public class {|#0:OrderPlacedHandler|} : IMessageHandler<OrderPlaced>
{
}");
        await AnalyzerVerifier.VerifyAnalyzerAsync(test, Expect().WithLocation(0).WithArguments("OrderPlaced"));
    }

    [Fact]
    public async Task Handler_PayloadCoveredByContext_NoDiagnostic()
    {
        var test = Source(@"
public class OrderPlaced { }

[JsonSerializable(typeof(OrderPlaced))]
public partial class AppJsonContext : JsonSerializerContext { }

public class OrderPlacedHandler : IMessageHandler<OrderPlaced>
{
}");
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    // Nesting the context inside the class that uses it is an ordinary shape, and the scan walked
    // namespaces only — so a nested context was never seen at all.
    //
    // These two cases have to be read together. Asserting only that a nested context suppresses the
    // diagnostic does NOT discriminate: when the context is invisible the activation gate never opens
    // and the analyzer reports nothing anyway, so that test passes either way. It is the ONLY-nested
    // context with an UNCOVERED type that separates them — the gate must open (the project does declare
    // a context) and the uncovered type must still be reported.
    [Fact]
    public async Task NestedContextIsTheOnlyContext_GateOpensAndTheUncoveredTypeIsStillReported()
    {
        var test = Source(@"
public class OrderPlaced { }
public class Other { }

public partial class Serialization
{
    [JsonSerializable(typeof(Other))]
    public partial class AppJsonContext : JsonSerializerContext { }
}

public class {|#0:OrderPlacedHandler|} : IMessageHandler<OrderPlaced>
{
}");
        await AnalyzerVerifier.VerifyAnalyzerAsync(test, Expect().WithLocation(0).WithArguments("OrderPlaced"));
    }

    [Fact]
    public async Task Handler_PayloadCoveredByANestedContext_NoDiagnostic()
    {
        var test = Source(@"
public class OrderPlaced { }

public partial class Serialization
{
    [JsonSerializable(typeof(OrderPlaced))]
    public partial class AppJsonContext : JsonSerializerContext { }
}

public class OrderPlacedHandler : IMessageHandler<OrderPlaced>
{
}");
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Handler_ButNoUserContext_NoDiagnostic()
    {
        // Gate: without any JsonSerializerContext the app is on the reflection path — stay silent.
        var test = Source(@"
public class OrderPlaced { }

public class OrderPlacedHandler : IMessageHandler<OrderPlaced>
{
}");
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SgGeneratedContext_CoversType_NoDiagnostic()
    {
        // The W3 SG-generated context emits [JsonSerializable] markers for the types it covers, so the
        // analyzer's existing coverage scan sees them — no PRAG2800 for a covered payload.
        var test = Source(@"
public class OrderPlaced { }

namespace App.Generated
{
    [JsonSerializable(typeof(global::OrderPlaced))]
    public partial class PragmaticJsonContext : JsonSerializerContext { }
}

public class OrderPlacedHandler : IMessageHandler<OrderPlaced>
{
}");
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task JobParameters_NotInContext_ReportsDiagnostic()
    {
        var test = Source(@"
public class EmailParams { }

[JsonSerializable(typeof(string))]
public partial class AppJsonContext : JsonSerializerContext { }

public class {|#0:SendEmailJob|} : IJob<EmailParams>
{
}");
        await AnalyzerVerifier.VerifyAnalyzerAsync(test, Expect().WithLocation(0).WithArguments("EmailParams"));
    }
}
