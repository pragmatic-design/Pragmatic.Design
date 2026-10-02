using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Query.Attributes;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A <c>[Query]</c> on a member that derives nothing says so.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ It derived nothing and said nothing. <c>SpecificationQueryTransform</c> answered
///         <c>null</c> six different ways, and the caller could not tell "not the shape I handle" from
///         "yours, and malformed". The second shipped as silence: the attribute compiled, no query was
///         generated, the build was green.
///     </para>
///     <para>
///         The transform is total now — every path carries a reason — which is also why the two files
///         leave the silent-drops count. The ratchet's budget went back to 221 because these were
///         triaged, not because it was raised.
///     </para>
/// </remarks>
public class ADeclinedSpecificationQuerySaysSoTests
{
    private const string Preamble = """
        using System;
        using System.Linq.Expressions;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Query.Attributes;
        using Pragmatic.Specification;

        namespace App.Sales;

        [Entity]
        public partial class Order : IEntity
        {
            public Guid PersistenceId { get; set; }
            public string Number { get; set; } = "";
        }
        """;

    /// <summary>A rule that is not static cannot be read without an instance, and is reported.</summary>
    [Fact]
    public void ARuleThatIsNotStatic_IsReported()
    {
        var reported = Diagnostics("""

            public class OrderRules
            {
                [Query<Order>]
                public Specification<Order> Recent() => null!;
            }
            """);

        reported.Should().Contain(d => d.Id == "PRAG0729" && d.GetMessage().Contains("not static"));
    }

    /// <summary>A member that returns something else is not a specification, and is reported.</summary>
    [Fact]
    public void AMemberThatIsNotASpecification_IsReported()
    {
        var reported = Diagnostics("""

            public class OrderRules
            {
                [Query<Order>]
                public static string Recent() => "";
            }
            """);

        reported.Should().Contain(d => d.Id == "PRAG0729"
            && d.GetMessage().Contains("does not return a Specification"));
    }

    /// <summary>A rule in a generic type cannot become a query type, and is reported.</summary>
    [Fact]
    public void ARuleInAGenericType_IsReported()
    {
        var reported = Diagnostics("""

            public class OrderRules<T>
            {
                [Query<Order>]
                public static Specification<Order> Recent() => null!;
            }
            """);

        reported.Should().Contain(d => d.Id == "PRAG0729" && d.GetMessage().Contains("generic type"));
    }

    /// <summary>
    ///     The control: a rule the generator does derive from is not reported.
    /// </summary>
    /// <remarks>
    ///     Without it, "a declined query is reported" is satisfied by a generator that reports every
    ///     <c>[Query]</c> on a member — including the ones that work, which is the whole feature.
    /// </remarks>
    [Fact]
    public void ARuleThatDerivesAQuery_IsNotReported()
    {
        var reported = Diagnostics("""

            public class OrderRules
            {
                [Query<Order>]
                public static Specification<Order> Recent() => null!;
            }
            """);

        reported.Should().NotContain(d => d.Id == "PRAG0729");
    }

    /// <summary>And the message names the member, so the reader knows which one.</summary>
    [Fact]
    public void TheMessage_NamesTheMember()
    {
        var reported = Diagnostics("""

            public class OrderRules
            {
                [Query<Order>]
                public static string Recent() => "";
            }
            """);

        reported.Should().Contain(d => d.Id == "PRAG0729" && d.GetMessage().Contains("Recent"));
    }

    private static IReadOnlyList<Diagnostic> Diagnostics(string declaration)
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            Preamble + declaration, References);

        return [.. GeneratorTestHelper.GetGeneratorDiagnostics(result)];
    }

    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<QueryAttribute<object>>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Specification.Specification<object>>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Result.IError>()
    ];
}
