using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     An operation can declare the two services the boundary registers <b>keyed</b>, and receives them.
/// </summary>
/// <remarks>
///     <para>
///         <c>DbContext</c> and <c>IUnitOfWork</c> are registered with
///         <c>AddKeyedScoped(typeof({Boundary}))</c>: they are the only two, as
///         <c>DbContextRegistrationTemplate</c> and <c>RepositoryRegistrationTemplate</c> show. Injected
///         <b>without a key</b>, no operation could declare them: resolution would fail at startup with a
///         message naming a generated invoker.
///     </para>
///     <para>
///         ⚠️ The workaround an application would reach for is a service locator — an
///         <c>IServiceProvider</c> field and <c>GetRequiredKeyedService&lt;T&gt;(typeof(XBoundary))</c>
///         repeated at every site. That is exactly what «Decide at compile time» in
///         <c>docs/CONVENTIONS.md</c> rules out: the generated code looking up at runtime what the
///         generator knows at compile time. No ratchet would count it, because it is not reflection.
///     </para>
/// </remarks>
public class BoundaryKeyedDependencyTests : ActionsGeneratorTestBase
{
    private const string Source = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Persistence.Repository;
        using Pragmatic.Result;

        namespace TestApp.Sales;

        [Pragmatic.Actions.Attributes.Boundary]
        public partial class SalesBoundary;

        [DomainAction]
        public partial class CountRowsAction : DomainAction<int>
        {
            private DbContext _db = null!;
            private IUnitOfWork _unitOfWork = null!;

            public override Task<Result<int, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<int, IError>.Success(0));
        }
        """;

    /// <summary>Both arrive with the key of the operation's boundary.</summary>
    [Fact]
    public void TheTwoKeyedServices_AreInjectedWithTheBoundaryKey()
    {
        var result = RunGeneratorWithEntities(Source);

        var invoker = GetGeneratedSource(result, "CountRowsAction.Invoker");
        invoker.Should().NotBeNull();

        invoker!.Should().Contain(
            "[global::Microsoft.Extensions.DependencyInjection.FromKeyedServices(typeof(global::TestApp.Sales.SalesBoundary))] global::Microsoft.EntityFrameworkCore.DbContext",
            "the DbContext is registered keyed only, and without the key resolution fails at startup");

        invoker.Should().Contain(
            "[global::Microsoft.Extensions.DependencyInjection.FromKeyedServices(typeof(global::TestApp.Sales.SalesBoundary))] global::Pragmatic.Persistence.Repository.IUnitOfWork",
            "and the same for the unit of work, which is the other of the two");
    }

    /// <summary>
    ///     The control: any other service stays unkeyed.
    /// </summary>
    /// <remarks>
    ///     Without it, «the keyed ones are keyed» would also be satisfied by an attribute on every
    ///     dependency — which would break resolution of everything else.
    /// </remarks>
    [Fact]
    public void AnOrdinaryService_IsNotKeyed()
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Actions.Abstractions;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Result;

            namespace TestApp.Sales;

            [Pragmatic.Actions.Attributes.Boundary]
            public partial class SalesBoundary;

            public interface IPricing { }

            [DomainAction]
            public partial class QuoteAction : DomainAction<int>
            {
                private IPricing _pricing = null!;

                public override Task<Result<int, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<int, IError>.Success(0));
            }
            """;

        var result = RunGenerator(source);

        var invoker = GetGeneratedSource(result, "QuoteAction.Invoker");
        invoker.Should().NotBeNull();
        invoker!.Should().Contain("global::TestApp.Sales.IPricing pricing");
        invoker.Should().NotContain("FromKeyedServices(typeof(global::TestApp.Sales.SalesBoundary))] global::TestApp.Sales.IPricing",
            "the key applies to the two services the boundary registers keyed, not to dependencies in general");

        // ⚠️ The absence of `FromKeyedServices` cannot be asserted on the whole file: the invoker already
        // receives its own unit of work with the boundary's key, rightly. The check is on the
        // parameter, not on the text.
    }

    /// <summary>
    ///     Where no boundary answers, the field cannot be resolved — and <c>PRAG0448</c> says so.
    /// </summary>
    /// <remarks>
    ///     Two boundaries in the same assembly, an operation that names neither and has no entity to
    ///     deduce it from: there is no key, the generated code would ask for the service without one,
    ///     and the application would not start, with an error naming a generated type. It is the shape
    ///     of <c>PRAG0447</c> for a composite, one level down.
    /// </remarks>
    [Fact]
    public void WhereNoBoundaryAnswers_ItIsReported()
    {
        var result = RunGeneratorWithEntities("""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.EntityFrameworkCore;
            using Pragmatic.Actions.Abstractions;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Result;

            namespace TestApp.Sales
            {
                [Pragmatic.Actions.Attributes.Boundary]
                public partial class SalesBoundary;
            }

            namespace TestApp.Billing
            {
                [Pragmatic.Actions.Attributes.Boundary]
                public partial class BillingBoundary;
            }

            namespace TestApp.Somewhere
            {
                [DomainAction]
                public partial class StrayAction : DomainAction<int>
                {
                    private DbContext _db = null!;

                    public override Task<Result<int, IError>> Execute(CancellationToken ct = default)
                        => Task.FromResult(Result<int, IError>.Success(0));
                }
            }
            """);

        HasDiagnostic(result, "PRAG0448").Should().BeTrue(
            "without a boundary the key cannot be written, and the failure would be at startup");
    }

    /// <summary>The control: where a boundary answers, silence.</summary>
    /// <remarks>
    ///     Without it, a diagnostic emitted always would satisfy the case above — and would say every
    ///     operation with a <c>DbContext</c> is wrong, the opposite of the intent.
    /// </remarks>
    [Fact]
    public void WhereABoundaryAnswers_NothingIsReported()
    {
        var result = RunGeneratorWithEntities(Source);

        HasDiagnostic(result, "PRAG0448").Should().BeFalse();
    }
}
