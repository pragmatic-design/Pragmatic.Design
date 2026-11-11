using System;
using System.Linq;
using Pragmatic.SourceGenerator.Tests.Features.Traits;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Endpoints;

/// <summary>
///     A typed pre- or post-processor on an operation's endpoint, a domain action or a mutation, compiles, and the
///     generated handler hands it the operation the request bound.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ The handler called <c>ProcessAsync(endpointContext, ct)</c> on the processor's class.
///         <c>IEndpointPreProcessor&lt;TAction&gt;</c> implements that overload only as the interface's default, so on the
///         class the call bound to the typed overload and the endpoint failed with CS1503; the same for
///         <c>IEndpointPostProcessor&lt;TAction&gt;</c>.
///     </para>
///     <para>
///         That the interface's default hands <c>context.Endpoint</c> to the typed overload is pinned by
///         <c>EndpointProcessorDispatchTests</c> in the Endpoints suite, and a request through a generated endpoint by
///         <c>ATypedProcessorReceivesTheEndpointTests</c> there. Here: the operation is what the handler puts in the
///         context, and the two operation templates compile.
///     </para>
/// </remarks>
public class ATypedProcessorOnAnOperationCompilesTests
{
    private const string Model = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Context;
        using Pragmatic.Endpoints.Processors;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;
        using Pragmatic.Result;

        namespace TestApp
        {
            [Boundary]
            public partial class RegistryBoundary { }

            [PragmaticDbContext("Registry")]
            public partial class RegistryDbContext { }
        }

        namespace TestApp.Entities
        {
            [Entity]
            [BelongsTo<TestApp.RegistryBoundary>]
            public partial class Customer : IEntity
            {
                public Guid Id { get; set; }
                public Guid PersistenceId { get => Id; set => Id = value; }
                public string Name { get; set; } = "";
            }
        }

        namespace TestApp.Actions
        {
            using TestApp.Entities;

            public sealed class PingPre : IEndpointPreProcessor<PingAction>
            {
                public ValueTask<PreProcessorResult> ProcessAsync(PingAction action, IEndpointContext context,
                    CancellationToken ct = default) => new(PreProcessorResult.Continue());
            }

            public sealed class PingPost : IEndpointPostProcessor<PingAction>
            {
                public ValueTask ProcessAsync(PingAction action, IEndpointContext context, object? result,
                    CancellationToken ct = default) => ValueTask.CompletedTask;
            }

            [DomainAction]
            [BelongsTo<TestApp.RegistryBoundary>]
            [Endpoint(HttpVerb.Get, "api/ping")]
            [PreProcessor<PingPre>]
            [PostProcessor<PingPost>]
            public partial class PingAction : DomainAction<string>
            {
                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<string, IError>>("pong");
            }

            public sealed class CreatePre : IEndpointPreProcessor<CreateCustomerMutation>
            {
                public ValueTask<PreProcessorResult> ProcessAsync(CreateCustomerMutation mutation, IEndpointContext context,
                    CancellationToken ct = default) => new(PreProcessorResult.Continue());
            }

            public sealed class CreatePost : IEndpointPostProcessor<CreateCustomerMutation>
            {
                public ValueTask ProcessAsync(CreateCustomerMutation mutation, IEndpointContext context, object? result,
                    CancellationToken ct = default) => ValueTask.CompletedTask;
            }

            [Mutation(Mode = MutationMode.Create)]
            [BelongsTo<TestApp.RegistryBoundary>]
            [Endpoint(HttpVerb.Post, "api/customers")]
            [PreProcessor<CreatePre>]
            [PostProcessor<CreatePost>]
            public partial class CreateCustomerMutation : Mutation<Customer>
            {
                public required string Name { get; init; }
            }
        }
        """;

    [Fact]
    public void TheOperationEndpoints_WithTypedProcessors_Compile()
    {
        var (mine, _) = TraitCompilationHarness.CompileAndSplitErrors(
            Model,
            // ⚠️ `_Infra.Endpoints.Registration.g.cs` is outside the net, as in AResultTypeOutsideTheOperationsNamespaceTests:
            // its rate-limiter policies need a reference this harness does not carry.
            static path => path.Contains("PingAction", StringComparison.Ordinal)
                           || path.Contains("CreateCustomerMutation", StringComparison.Ordinal)
                           || path.EndsWith("TestSource.cs", StringComparison.Ordinal));

        mine.Should().BeEmpty(TraitCompilationHarness.FormatErrors(mine));
    }

    /// <summary>
    ///     The control: the net catches the files that call the processors, so an empty list above is about them and
    ///     not about a filter that matches nothing.
    /// </summary>
    [Fact]
    public void TheHandlers_CallTheProcessors_WithTheOperationInTheContext()
    {
        var sources = TraitCompilationHarness.Generate(Model).Sources;

        foreach (var operation in new[] { "PingAction", "CreateCustomerMutation" })
        {
            var handler = sources.Where(s => s.Key.Contains(operation, StringComparison.Ordinal))
                .Select(s => s.Value)
                .Single(text => text.Contains("preProcessor", StringComparison.Ordinal));

            handler.Should().Contain("EndpointContext(httpContext,", "the processors read the operation off the context");
            handler.Should().Contain(", action);", "the operation the request bound is the context's endpoint");
            handler.Should().Contain("postProcessor", "the post-processor is called too");
        }
    }
}
