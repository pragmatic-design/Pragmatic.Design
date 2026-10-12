using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     The generated endpoint code compiles in a project without implicit usings (#202).
/// </summary>
/// <remarks>
///     <c>_Infra.Endpoints.Registration.g.cs</c> called <c>services.LastOrDefault(…)</c> by its short name and declared no
///     <c>using System.Linq;</c>: it compiled only where the consumer's implicit usings brought one, and a project
///     with <c>&lt;ImplicitUsings&gt;disable&lt;/ImplicitUsings&gt;</c> got CS1061 in code it never wrote. The in-memory
///     compilation here has no implicit usings, so every generated file must name what it calls.
/// </remarks>
public class TheEndpointRegistrationCompilesWithoutImplicitUsingsTests : EndpointsGeneratorTestBase
{
    private const string Source = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Result;

        namespace TestApp.Notes;

        [DomainAction]
        [Endpoint(HttpVerb.Post, "api/notes")]
        public partial class AddNoteAction : DomainAction<int>
        {
            public required string Text { get; init; }

            public override Task<Result<int, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<int, IError>.Success(0));
        }
        """;

    [Fact]
    public void AnEndpointModule_CompilesWithNoUsingItDidNotDeclare()
    {
        // What the generated code calls into at run time, which the generator tests' references leave out.
        var result = RunGenerator(Source,
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Endpoints.Binding.RequestJson)),
            GeneratorTestHelper.FromTypeAssembly(typeof(System.Buffers.ReadOnlySequence<>)),
            GeneratorTestHelper.FromTypeAssembly(typeof(Uri)),
            GeneratorTestHelper.FromTypeAssembly(typeof(Enumerable)),
            GeneratorTestHelper.FromTypeAssembly(typeof(System.Threading.RateLimiting.RateLimitPartition)));

        GetCompilationErrors(result).Select(d => $"{d.Location.SourceTree?.FilePath}: {d.GetMessage()}")
            .Should().BeEmpty("generated code names what it calls, whatever usings the consumer's project has");
    }
}
