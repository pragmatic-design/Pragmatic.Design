using Microsoft.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Actions.Attributes;
using Pragmatic.Composition.Attributes;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Endpoints.Base;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Endpoints;

/// <summary>
///     A hand-written endpoint can declare the boundary's <c>DbContext</c> or <c>IUnitOfWork</c> as a
///     field, and the generated binding resolves it with the boundary as its key.
/// </summary>
/// <remarks>
///     <para>
///         Those two services are the only ones a boundary registers keyed by its own type
///         (<c>AddKeyedScoped&lt;DbContext&gt;(typeof(TBoundary), …)</c>), so a parameter resolved
///         unkeyed finds nothing. An operation's invoker writes <c>[FromKeyedServices]</c> for them,
///         and the endpoint binding does the same for a declared dependency, so a hand-written endpoint
///         does not reach for <c>IServiceProvider</c> and repeat <c>typeof(TBoundary)</c> by hand.
///     </para>
///     <para>
///         The mechanism is the binding's own: <c>BoundParameter</c> carries a <c>ServiceKey</c> and
///         <c>RequestDelegateRenderer</c> renders <c>BindingSource.KeyedService</c>.
///     </para>
/// </remarks>
public class TheEndpointDeclaresItsBoundaryKeyedServiceTests
{
    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<BoundaryAttribute>(),
        GeneratorTestHelper.FromType<ModuleAttribute>(),
        GeneratorTestHelper.FromType<EndpointAttribute>(),
        GeneratorTestHelper.FromTypeAssembly(typeof(Endpoint<>)),
        GeneratorTestHelper.FromTypeAssembly(typeof(DbContext)),
        // Without it ILogger<T> is an error type, the control below has nothing to be unkeyed, and the
        // test measures a missing reference rather than the generator.
        GeneratorTestHelper.FromTypeAssembly(typeof(Microsoft.Extensions.Logging.ILogger<>))
    ];

    private static string EndpointSourceFor(string module)
    {
        var source = $$"""
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.EntityFrameworkCore;
            using Pragmatic.Composition.Attributes;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;
            using Microsoft.Extensions.Logging;

            namespace Contoso.Billing;

            {{module}}

            [Endpoint(HttpVerb.Get, "api/ledger/balance")]
            public partial class ReadBalanceEndpoint : Endpoint<string>
            {
                private DbContext _db = null!;
                private ILogger<ReadBalanceEndpoint> _logger = null!;

                public override Task<Result<string>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<string>.Success("0"));
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, References);

        return GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Single(f => f.Key.Contains("ReadBalanceEndpoint.Endpoint")).Value;
    }

    [Fact]
    public void TheBoundarysDbContext_IsResolvedWithTheBoundaryAsItsKey()
    {
        var generated = EndpointSourceFor("""
            [Module]
            public sealed class BillingModule;
            """);

        generated.Should().Contain("GetRequiredKeyedService", "DbContext is registered keyed by the boundary");
        generated.Should().Contain("typeof(global::Contoso.Billing.BillingBoundary)",
            "and the key is the boundary that answers for this module");
    }

    /// <summary>
    ///     The control, in the same generated file: a service that is not boundary-keyed stays unkeyed.
    /// </summary>
    /// <remarks>
    ///     Without it, "key the boundary-keyed services" and "key everything" produce the same green on
    ///     the assertion above — and keying everything would break every ordinary dependency, since
    ///     nothing else is registered with a key.
    /// </remarks>
    [Fact]
    public void AnOrdinaryService_IsStillResolvedUnkeyed()
    {
        var generated = EndpointSourceFor("""
            [Module]
            public sealed class BillingModule;
            """);

        generated.Should().Contain("GetRequiredService<global::Microsoft.Extensions.Logging.ILogger<",
            "ILogger is registered without a key, and asking for a keyed one would find nothing");
    }

    /// <summary>
    ///     The other control: no boundary answers, so the key would be a guess and is not written.
    /// </summary>
    /// <remarks>
    ///     An assembly declaring several boundaries, or none, cannot say which one a hand-written
    ///     endpoint belongs to. Keying it with the wrong one fails at the first request instead of the
    ///     first build, which is worse than leaving it as it was.
    /// </remarks>
    [Fact]
    public void WithNoBoundaryAnswering_TheParameterStaysUnkeyed()
    {
        var generated = EndpointSourceFor("");

        generated.Should().NotContain("GetRequiredKeyedService",
            "nothing here names a boundary, so there is no key to write");
    }
}
