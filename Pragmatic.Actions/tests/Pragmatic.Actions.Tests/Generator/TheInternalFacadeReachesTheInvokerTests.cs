using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     An operation may inject its own module's <b>internal</b> boundary facade, and the invoker that
///     receives it still compiles — as an <c>internal</c> invoker taking it on the constructor.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <c>I{Boundary}InternalActions</c> is emitted <c>internal</c> — it is the facade that runs
///         as an internal call, and making it public would hand every module the right to skip the
///         permissions of everything it reaches. A <c>public</c> invoker with a <c>public</c>
///         constructor cannot take an internal parameter: <c>CS0051</c>, in a file the consumer cannot
///         edit.
///     </para>
///     <para>
///         ⚠️ Taking the parameter off the signature and assigning the field from the
///         <see cref="System.IServiceProvider" /> instead is a service locator, which only moves the
///         failure from container validation to first construction. The constraint is not in this
///         invoker: a <b>host</b> that named these types, in another assembly, when writing out DI
///         registrations would need them public. The host calls the module's own registration
///         extension instead, so nothing outside the module names them, and an invoker can do what an
///         accessibility modifier is for — follow its least accessible dependency.
///     </para>
/// </remarks>
public class TheInternalFacadeReachesTheInvokerTests : ActionsGeneratorTestBase
{
    private const string Source = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Result;

        namespace TestApp.Sales;

        [Pragmatic.Actions.Attributes.Boundary]
        public partial class SalesBoundary;

        [DomainAction]
        public partial class ReconcileAction : DomainAction<int>
        {
            private ISalesInternalActions _sales = null!;

            public override Task<Result<int, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<int, IError>.Success(0));
        }
        """;

    /// <summary>The claim: internal invoker, constructor injection, no locator.</summary>
    [Fact]
    public void TheInvokerTakingIt_IsInternalAndTakesItThroughTheConstructor()
    {
        var result = RunGeneratorWithEntities(Source);

        var invoker = GetGeneratedSource(result, "ReconcileAction.Invoker");

        invoker.Should().NotBeNull();
        invoker!.Should().Contain("internal sealed class Invoker",
            "a type cannot be more accessible than what its constructor takes — that is CS0051");
        invoker.Should().Contain("global::TestApp.Sales.ISalesInternalActions sales",
            "and with the accessibility right, the dependency goes back on the constructor — fully "
            + "qualified, because the interface does not exist yet and an error symbol cannot qualify itself");
        invoker.Should().NotContain("GetRequiredService<global::TestApp.Sales.ISalesInternalActions>",
            "the service locator existed only because the type had to stay public");
    }

    /// <summary>
    ///     The control: an invoker with nothing internal about it stays <c>public</c>.
    /// </summary>
    /// <remarks>
    ///     Without it, "follow the least accessible dependency" is satisfied by making every invoker
    ///     internal — invisible here, and breaking any consumer that names one.
    /// </remarks>
    [Fact]
    public void AnOrdinaryInvoker_StaysPublic()
    {
        var result = RunGeneratorWithEntities(OrdinaryDependencySource);

        var invoker = GetGeneratedSource(result, "StampAction.Invoker");

        invoker.Should().NotBeNull();
        invoker!.Should().Contain("public sealed class Invoker");
    }

    /// <summary>
    ///     The second control: the <b>public</b> facade is still taken through the constructor.
    /// </summary>
    /// <remarks>
    ///     Without it, "put the internal facade on the signature" would be satisfied by a change that
    ///     also moved the public one somewhere else, for the case that never had a problem.
    /// </remarks>
    [Fact]
    public void ThePublicFacade_IsStillAConstructorParameter()
    {
        var result = RunGeneratorWithEntities("""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Actions.Abstractions;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Result;

            namespace TestApp.Sales;

            [Pragmatic.Actions.Attributes.Boundary]
            public partial class SalesBoundary;

            [DomainAction]
            public partial class ReportAction : DomainAction<int>
            {
                private ISalesActions _sales = null!;

                public override Task<Result<int, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<int, IError>.Success(0));
            }
            """);

        var invoker = GetGeneratedSource(result, "ReportAction.Invoker");

        invoker.Should().NotBeNull();
        invoker!.Should().Contain("global::TestApp.Sales.ISalesActions sales",
            "it is public, so the constructor can take it and the container can validate it");
        invoker.Should().Contain("public sealed class Invoker",
            "and the invoker that takes only public things stays public");
        invoker.Should().NotContain("GetRequiredService<global::TestApp.Sales.ISalesActions>");
    }

    /// <summary>And the third control: an ordinary dependency is untouched by any of this.</summary>
    [Fact]
    public void AnOrdinaryDependency_IsStillAConstructorParameter()
    {
        var result = RunGeneratorWithEntities(OrdinaryDependencySource);

        var invoker = GetGeneratedSource(result, "StampAction.Invoker");

        invoker.Should().NotBeNull();
        invoker!.Should().Contain("global::TestApp.Sales.IClock clock");
        invoker.Should().NotContain("GetRequiredService<global::TestApp.Sales.IClock>");
    }

    private const string OrdinaryDependencySource = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Result;

        namespace TestApp.Sales;

        [Pragmatic.Actions.Attributes.Boundary]
        public partial class SalesBoundary;

        public interface IClock { DateTime Now { get; } }

        [DomainAction]
        public partial class StampAction : DomainAction<int>
        {
            private IClock _clock = null!;

            public override Task<Result<int, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<int, IError>.Success(0));
        }
        """;
}
