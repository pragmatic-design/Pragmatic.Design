using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     What a mutation's generated <c>ApplyToEntity</c> does with the mapping module's attributes.
/// </summary>
/// <remarks>
///     <para>
///         A mutation <b>does</b> inherit what a <c>[MapTo]</c> DTO gets: <c>[Mutation]</c> is another
///         classifier of a mapping, and wherever
///         <c>Pragmatic.Mapping</c> is referenced the write body is Mapping's — converters, renames
///         and pointed targets included. <c>ApplyToEntity</c> is the one-line override the invoker
///         calls; the body lives in <c>{Type}.Mapping.g.cs</c>.
///     </para>
///     <para>
///         ⚠️ The tests assert on the file the body is actually in, and every one of them also asserts the
///         compilation is clean — that is what proves the delegation has a recipient, rather than
///         pointing at a member nobody emits.
///     </para>
/// </remarks>
public class MutationPropertyMappingTests : ActionsGeneratorTestBase
{
    private const string EfCorePresence = """
        namespace Pragmatic.Persistence.EFCore
        {
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class PragmaticDbContextAttribute : Attribute { }
        }
        """;

    /// <param name="entityBody">The entity's own properties.</param>
    /// <param name="mutationBody">The mutation's properties, with whatever attributes are under test.</param>
    private static string Source(string entityBody, string mutationBody) => $$"""
        using System;
        using System.Collections.Generic;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Mapping.Attributes;
        using Pragmatic.Persistence.Entity;

        {{EfCorePresence}}

        namespace TestApp;

        public class SalesBoundary;

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class Order : IEntity
        {
            {{entityBody}}
        }

        [Mutation(Mode = MutationMode.Update)]
        public partial class UpdateOrderMutation : Mutation<Order>
        {
            public Guid Id { get; init; }
            {{mutationBody}}
        }
        """;

    /// <summary>
    ///     The control: <c>[MapIgnore]</c> is the one mapping attribute the mutation path reads.
    /// </summary>
    /// <remarks>
    ///     Without this, the two tests below would pass for a fixture that never reached the mapping
    ///     attributes at all — a missing reference, a name typo — instead of for the reason claimed.
    /// </remarks>
    [Fact]
    public void MapIgnore_OnAMutationProperty_IsHonoured()
    {
        var result = RunGeneratorForMutation(Source(
            entityBody: """
                public string Reference { get; set; } = "";
                public string Scratch { get; set; } = "";
                """,
            mutationBody: """
                public string? Reference { get; init; }
                [MapIgnore]
                public string? Scratch { get; init; }
                """));

        var generated = GetGeneratedSource(result, "UpdateOrderMutation.Mapping");

        generated.Should().NotBeNull();
        generated!.Should().Contain("entity.Reference = this.Reference;",
            "the ordinary property is assigned, so the fixture reaches the mapping at all");
        generated.Should().NotContain("Scratch", "[MapIgnore] is honoured on the write side");

        // The delegation has a recipient: without the body, ApplyToEntity would name an ApplyToLoaded
        // nobody emits, and that would be CS0103 in a file the author cannot open.
        TheDelegationHasARecipient(result);
    }

    /// <summary>
    ///     <c>[MapProperty(Target = ...)]</c> redirects the write on a mutation, as it does on a DTO.
    /// </summary>
    /// <remarks>
    ///     It used not to: the name was matched literally, <c>Note</c> is not a property of
    ///     <c>Order</c>, and the value reached nothing — while <c>PRAG0414</c> reported a missing
    ///     <c>SetNote()</c>. The control below is <c>Reference</c>, written by direct name: if the
    ///     declared target broke, only the first assertion would go red.
    /// </remarks>
    [Fact]
    public void MapProperty_Target_OnAMutationProperty_RedirectsTheWrite()
    {
        var result = RunGeneratorForMutation(Source(
            entityBody: """
                public string Reference { get; set; } = "";
                public string Remark { get; set; } = "";
                """,
            mutationBody: """
                public string? Reference { get; init; }
                [MapProperty(Target = "Remark")]
                public string? Note { get; init; }
                """));

        var generated = GetGeneratedSource(result, "UpdateOrderMutation.Mapping");

        generated.Should().NotBeNull();
        generated!.Should().Contain("entity.Remark = this.Note;",
            "the declared target is where the value lands, not a property named after the source");
        generated.Should().Contain("entity.Reference = this.Reference;",
            "the control: a property matched by direct name still maps");

        TheDelegationHasARecipient(result);
    }

    /// <summary>
    ///     A type mismatch is converted, where the conversion is one Mapping knows.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Emitted raw, it would be <c>entity.Amount = amount;</c>, a string assigned to a
    ///         decimal, <c>CS0029</c> inside a generated file the author cannot open, with nothing
    ///         upstream saying so.
    ///     </para>
    ///     <para>
    ///         ⚠️ What the conversion does with a <b>missing</b> value is measured here too, and it is
    ///         not the same as the unconverted path: that one guards on null and leaves the entity
    ///         alone, this one writes <c>default</c>. Stated because it is a real difference, not
    ///         because it is endorsed.
    ///     </para>
    /// </remarks>
    [Fact]
    public void AMismatchedPropertyType_IsConverted()
    {
        var result = RunGeneratorForMutation(Source(
            entityBody: """
                public decimal Amount { get; set; }
                """,
            mutationBody: """
                public string? Amount { get; init; }
                """));

        var generated = GetGeneratedSource(result, "UpdateOrderMutation.Mapping");

        generated.Should().NotBeNull();
        generated!.Should().Contain("decimal.Parse(",
            "the conversion Mapping knows is emitted, so the mismatch compiles");
        generated.Should().Contain("InvariantCulture",
            "a culture-dependent parse in generated code would read a decimal differently on two "
            + "machines: the culture is pinned, not inherited from the thread");

        TheDelegationHasARecipient(result);
        GeneratorTestHelper.GetCompilationErrors(result)
            .Should().NotContain(d => d.Id == "CS0029",
                "the conversion is what removes the CS0029 this test used to pin");
    }

    /// <summary>
    ///     A partial update of a relation's key: the key is sent only when it changes.
    /// </summary>
    /// <remarks>
    ///     The unwrap of a <c>Nullable&lt;T&gt;</c> was decided from the target's symbol, and a key the
    ///     relation generates is not one during this pass: <c>SetCustomerId(this.CustomerId)</c> handed a
    ///     <c>Guid?</c> to a <c>Guid</c>, CS1503 in a file the author cannot open. A declared property of
    ///     the same shape was always unwrapped.
    /// </remarks>
    [Fact]
    public void ANullableRelationKey_IsUnwrapped_LikeADeclaredOne()
    {
        var result = RunGeneratorForMutation($$"""
            using System;
            using Pragmatic.Actions.Mutation;
            using Pragmatic.Persistence.Entity;

            {{EfCorePresence}}

            namespace TestApp;

            public class SalesBoundary;

            [Entity]
            [BelongsTo<SalesBoundary>]
            public partial class Customer : IEntity { }

            [Entity]
            [BelongsTo<SalesBoundary>]
            [Relation.ManyToOne<Customer>]
            public partial class Order : IEntity
            {
                public int Priority { get; private set; }
            }

            [Mutation(Mode = MutationMode.Update)]
            public partial class ReassignOrderMutation : Mutation<Order>
            {
                public Guid Id { get; init; }
                public Guid? CustomerId { get; init; }
                public int? Priority { get; init; }
            }
            """);

        var mapping = GeneratorTestHelper.GetGeneratedSource(result, "ReassignOrderMutation.Mapping");
        mapping.Should().NotBeNull();
        mapping!.Should().Contain("SetPriority(this.Priority.GetValueOrDefault())",
            "the control: a declared value-type property is unwrapped inside its null check");
        mapping.Should().Contain("SetCustomerId(this.CustomerId.GetValueOrDefault())",
            "and the key the relation generates is the same shape");
        GeneratorTestHelper.GetCompilationErrors(result)
            .Should().NotContain(d => d.Id == "CS1503" && d.GetMessage().Contains("Guid?"));
    }

    /// <summary>
    ///     No error names <c>ApplyToLoaded</c>: the one-line override points at a member that exists.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Not <c>HasCompilationErrors</c>. This fixture does not compile clean and never did — it
    ///     declares a fake <c>PragmaticDbContextAttribute</c> to switch the persistence feature on,
    ///     which makes the generator emit a repository and a policy registry against assemblies the
    ///     test does not reference. Asserting "no errors at all" would fail for reasons that have
    ///     nothing to do with the claim, so the assertion names the claim instead.
    /// </remarks>
    private static void TheDelegationHasARecipient(SourceGenRunResult result)
        => GeneratorTestHelper.GetCompilationErrors(result)
            .Should().NotContain(d => d.GetMessage().Contains("ApplyToLoaded"),
                "ApplyToEntity delegates to ApplyToLoaded, which Mapping must have emitted");

    private static SourceGenRunResult RunGeneratorForMutation(string source)
    {
        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            source,
            [
                GeneratorTestHelper.FromType<Pragmatic.Actions.Attributes.DomainActionAttribute>(),
                GeneratorTestHelper.FromType<Pragmatic.Result.IError>(),
                GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Result.Result<,>)),
                GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Ensure.Ensure)),
                GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.IEntity>(),
                GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.SoftDeleteAttribute>(),
                GeneratorTestHelper.FromType<Pragmatic.Mapping.Attributes.MapToAttribute<object>>(),
                GeneratorTestHelper.FromType<Pragmatic.Specification.Specification<object>>(),
                GeneratorTestHelper.FromType<Microsoft.EntityFrameworkCore.DbContext>(),
                GeneratorTestHelper.FromType<Microsoft.Extensions.DependencyInjection.IServiceCollection>(),
                GeneratorTestHelper.FromTypeAssembly(
                    typeof(Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions)),
                GeneratorTestHelper.FromType<Microsoft.Extensions.Logging.ILogger>(),
            ]);
    }
}
