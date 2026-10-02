using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     The id a mutation loads its row by: generated when absent, checked when declared (PRAG0435).
/// </summary>
/// <remarks>
///     <para>
///         Create makes a row; every other mode finds one, and the generated invoker finds it by the
///         mutation's <c>Id</c>. Without one the invoker could only emit
///         <c>LoadEntityAsync => Task.FromResult&lt;TEntity?&gt;(null)</c> — it would compile, ship, and
///         match nothing on every call. So the generator writes the property, since the mode implies
///         it.
///     </para>
///     <para>
///         The diagnostic keeps the case it is still needed for: an <c>int Id</c> against a
///         <c>Guid</c>-keyed entity is the author's own declaration, and it either breaks inside a
///         generated file they cannot open or — where a conversion exists — compiles and matches no row.
///     </para>
/// </remarks>
public class MutationIdPropertyTests : ActionsGeneratorTestBase
{
    private static string Source(string mode, string idDeclaration) => $$"""
        using System;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Persistence.Entity;

        namespace TestApp.Catalog;

        [Entity]
        public partial class Product : IEntity
        {
            public string Name { get; set; } = string.Empty;
        }

        [Mutation(Mode = MutationMode.{{mode}})]
        public partial class {{mode}}ProductMutation : Mutation<Product>
        {
            {{idDeclaration}}
            public string? Name { get; init; }
        }
        """;

    /// <summary>
    ///     A mode that loads a row gets its id written for it, and is not reported for lacking one.
    /// </summary>
    [Theory]
    [InlineData("Update")]
    [InlineData("Delete")]
    [InlineData("Restore")]
    public void AModeThatLoadsARow_WithNoId_HasOneGenerated(string mode)
    {
        var result = RunGenerator(Source(mode, idDeclaration: ""));

        HasDiagnostic(result, "PRAG0435").Should().BeFalse(
            "the id is implied by the mode, so it is written rather than demanded");

        GetGeneratedSource(result, $"{mode}ProductMutation.Id").Should()
            .Contain("public required global::System.Guid Id { get; init; }",
                "the invoker loads by it, so it has to exist and to be the entity's key type");
    }

    [Theory]
    [InlineData("Update")]
    [InlineData("Delete")]
    [InlineData("Restore")]
    public void AModeThatLoadsARow_WithAMatchingId_IsAccepted(string mode)
        => HasDiagnostic(
                RunGenerator(Source(mode, "public required Guid Id { get; init; }")), "PRAG0435")
            .Should().BeFalse();

    /// <summary>
    ///     An id of the wrong type addresses nothing, and saying so is the point.
    /// </summary>
    [Fact]
    public void AnIdOfTheWrongType_IsReported()
        => HasDiagnostic(
                RunGenerator(Source("Update", "public required int Id { get; init; }")), "PRAG0435")
            .Should().BeTrue("an int cannot address a Guid-keyed row");

    /// <summary>
    ///     Create has nothing to address: it makes the row.
    /// </summary>
    /// <remarks>
    ///     Without this the check would condemn every create mutation in every application — the most
    ///     common mutation there is, and the one that correctly has no id.
    /// </remarks>
    [Fact]
    public void Create_WithNoId_IsAccepted()
        => HasDiagnostic(RunGenerator(Source("Create", idDeclaration: "")), "PRAG0435")
            .Should().BeFalse();
}
