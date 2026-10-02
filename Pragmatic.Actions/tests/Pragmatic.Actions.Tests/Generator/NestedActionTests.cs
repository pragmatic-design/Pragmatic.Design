using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     A <c>[DomainAction]</c> declared inside another type.
///     <para>
///         The generated partial does not reproduce the containing-type chain, so it declares a
///         namespace-level class of the same simple name. That class is not the one the author wrote:
///         the invoker nested inside it cannot override a base generic over the user's actual (nested)
///         type, and the build fails with CS0534 and CS0115 pointing at a file nobody wrote.
///     </para>
///     <para>
///         Reported rather than supported. Emitting the chain would change how every generated artifact
///         opens its type — for mutations, queries and endpoints too, not only domain actions — and no
///         code in this repository declares an action that way. A message that names the cause is worth
///         more than two compiler errors on generated source.
///     </para>
/// </summary>
public class NestedActionTests : ActionsGeneratorTestBase
{
    private const string Usings = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Result;
        """;

    private const string Nested = $$"""
        {{Usings}}

        namespace TestApp.Billing;

        [Boundary]
        public partial class BillingBoundary;

        public partial class Outer
        {
            [DomainAction]
            public partial class IssueRefundAction : DomainAction<Guid>
            {
                public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
            }
        }
        """;

    private const string TopLevel = $$"""
        {{Usings}}

        namespace TestApp.Billing;

        [Boundary]
        public partial class BillingBoundary;

        [DomainAction]
        public partial class IssueRefundAction : DomainAction<Guid>
        {
            public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
        }
        """;

    [Fact]
    public void ActionNestedInAnotherType_ReportsPrag0408()
    {
        var result = RunGenerator(Nested);

        HasDiagnostic(result, "PRAG0408").Should().BeTrue();
    }

    // The generator must also stop, not report and carry on: emitting the invoker anyway would add the
    // two compiler errors this diagnostic exists to replace.
    [Fact]
    public void ActionNestedInAnotherType_ProducesNoInvoker()
    {
        var result = RunGenerator(Nested);

        GetGeneratedSource(result, "IssueRefundAction.Invoker").Should().BeNull();
    }

    [Fact]
    public void ActionAtNamespaceLevel_IsUnaffected()
    {
        var result = RunGenerator(TopLevel);

        HasDiagnostic(result, "PRAG0408").Should().BeFalse();
        GetGeneratedSource(result, "IssueRefundAction.Invoker").Should().NotBeNull();
    }
}
