using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     The boundary interface an operation injects is emitted <b>fully qualified</b>, like every other
///     type on the generated constructor.
/// </summary>
/// <remarks>
///     <para>
///         The interface is written by this same generator, so when the field is read it is still an
///         error type, and <c>ToDisplayString(FullyQualifiedFormat)</c> on an error type returns the
///         bare name the author wrote. The recognition is by name, so the emission was by name too.
///     </para>
///     <para>
///         ⚠️ It binds anyway whenever the operation's namespace nests under the boundary's — which is
///         the ordinary layout, and the reason this was invisible. An operation placed outside that
///         namespace produces a generated file naming a type the author never wrote unqualified, in a
///         file the author cannot edit.
///     </para>
/// </remarks>
public class BoundaryInterfaceIsEmittedQualifiedTests : ActionsGeneratorTestBase
{
    /// <summary>The operation lives outside the boundary's namespace, which is where the bare name fails.</summary>
    [Fact]
    public void TheInjectedBoundaryInterface_IsQualified()
    {
        var result = RunGeneratorWithEntities("""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Actions.Abstractions;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Result;
            using TestApp.Sales;

            namespace TestApp.Sales
            {
                [Pragmatic.Actions.Attributes.Boundary]
                public partial class SalesBoundary;
            }

            namespace TestApp.Reporting
            {
                [DomainAction]
                [BelongsTo<TestApp.Sales.SalesBoundary>]
                public partial class SummariseSalesAction : DomainAction<int>
                {
                    private ISalesActions _sales = null!;

                    public override Task<Result<int, IError>> Execute(CancellationToken ct = default)
                        => Task.FromResult(Result<int, IError>.Success(0));
                }
            }
            """);

        var invoker = GetGeneratedSource(result, "SummariseSalesAction.Invoker");

        invoker.Should().NotBeNull();
        invoker!.Should().Contain("global::TestApp.Sales.ISalesActions",
            "every other type on that constructor is fully qualified, and this one binds by accident "
            + "only while the operation's namespace nests under the boundary's");
    }

    /// <summary>
    ///     The control: a type that is not a boundary interface keeps the name the compiler resolves.
    /// </summary>
    /// <remarks>
    ///     Without it, "qualify the boundary interface" would be satisfied by prefixing the boundary's
    ///     namespace onto anything unresolved — which would break every ordinary dependency whose type
    ///     lives somewhere else entirely.
    /// </remarks>
    [Fact]
    public void AnOrdinaryDependency_KeepsItsOwnNamespace()
    {
        var result = RunGeneratorWithEntities("""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Actions.Abstractions;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Result;
            using TestApp.Infrastructure;

            namespace TestApp.Sales
            {
                [Pragmatic.Actions.Attributes.Boundary]
                public partial class SalesBoundary;
            }

            namespace TestApp.Infrastructure
            {
                public interface IClock { DateTime Now { get; } }
            }

            namespace TestApp.Reporting
            {
                [DomainAction]
                [BelongsTo<TestApp.Sales.SalesBoundary>]
                public partial class StampSalesAction : DomainAction<int>
                {
                    private IClock _clock = null!;

                    public override Task<Result<int, IError>> Execute(CancellationToken ct = default)
                        => Task.FromResult(Result<int, IError>.Success(0));
                }
            }
            """);

        var invoker = GetGeneratedSource(result, "StampSalesAction.Invoker");

        invoker.Should().NotBeNull();
        invoker!.Should().Contain("global::TestApp.Infrastructure.IClock",
            "it resolves, so it is qualified from the symbol and not from the boundary");
        invoker.Should().NotContain("global::TestApp.Sales.IClock");
    }
}
