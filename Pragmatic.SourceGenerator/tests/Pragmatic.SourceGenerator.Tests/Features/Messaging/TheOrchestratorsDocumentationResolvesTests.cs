using System.Linq;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Messaging;

/// <summary>
///     The generated orchestrator's XML documentation names the state enum by a reference that resolves.
/// </summary>
/// <remarks>
///     <para>
///         <c>SagaOrchestratorTemplate</c> must not write <c>&lt;see cref="{StateTypeShortName}"/&gt;</c>
///         — the enum's <b>simple</b> name — into a file whose namespace is the saga's. When the enum
///         lives anywhere else (an <c>Enums</c> namespace, say, which is where this framework's own
///         convention puts it) the reference resolves to nothing: <b>CS1574</b>, which is a warning until
///         a project generates a documentation file with <c>--warnaserror</c>, and then it is a build
///         error in a file the author cannot edit.
///     </para>
///     <para>
///         Casework has that shape: <c>CaseProcess</c> in <c>Casework.Intake.Enums</c>, the saga in
///         <c>Casework.Intake.Infrastructure.Sagas</c>, and a simple-name reference stops the module
///         compiling on the generated orchestrator's summary line.
///     </para>
/// </remarks>
public class TheOrchestratorsDocumentationResolvesTests
{
    private const string SagaStubs = """
        namespace Pragmatic.Messaging.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class MessageHandlerAttribute : System.Attribute { public int Order { get; set; } }

            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class SagaAttribute<TState> : System.Attribute where TState : struct, System.Enum { }

            [System.AttributeUsage(System.AttributeTargets.Method)]
            public sealed class SagaStartAttribute : System.Attribute { }

            [System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = true)]
            public sealed class InStateAttribute : System.Attribute
            {
                public InStateAttribute(object state) { State = state; }
                public object State { get; }
                public object? NextState { get; set; }
            }

            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class CorrelationKeyAttribute : System.Attribute { }
        }
        """;

    /// <summary>The saga in one namespace, its state enum in another — the ordinary arrangement.</summary>
    private const string Source = SagaStubs + """

        namespace App.Enums
        {
            public enum CaseProcess { Waiting, Done }
        }

        namespace App.Sagas
        {
            public sealed record ItHappened
            {
                [Pragmatic.Messaging.Attributes.CorrelationKey]
                public System.Guid Key { get; init; }
            }

            [Pragmatic.Messaging.Attributes.Saga<App.Enums.CaseProcess>]
            public sealed partial class TheProcess
            {
                [Pragmatic.Messaging.Attributes.SagaStart]
                [Pragmatic.Messaging.Attributes.InState(App.Enums.CaseProcess.Waiting,
                    NextState = App.Enums.CaseProcess.Done)]
                public void OnIt(ItHappened @event) { }
            }
        }
        """;

    [Fact]
    public void TheSummary_NamesTheStateTypeSoItResolves()
    {
        var orchestrator = Orchestrator();

        orchestrator.Should().Contain("<see cref=\"App.Enums.CaseProcess\"/>",
            "a cref is resolved from the generated file's own namespace, and the enum is not in it");
    }

    /// <summary>
    ///     The control: the reference is a <c>cref</c> and not plain text, so the documentation still
    ///     links.
    /// </summary>
    [Fact]
    public void TheSummary_StillLinksRatherThanSpelling()
    {
        Orchestrator().Should().NotContain("State type: App.Enums.CaseProcess.",
            "the fix is a resolvable reference, not the removal of the reference");
    }

    private static string Orchestrator()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Source, []);
        var generated = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);

        var match = generated.FirstOrDefault(pair => pair.Key.Contains("Orchestrator"));
        match.Value.Should().NotBeNull(
            $"the orchestrator is generated; generated: {string.Join("; ", generated.Keys)}");

        return match.Value;
    }
}
