using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     How the invoker builds the entity: the declared constructor, otherwise the generated factory.
/// </summary>
/// <remarks>
///     <para>
///         `new` skips what only `Create` does — the key, `[DefaultValue]`, `[ComputedDefault]` and the
///         audit dates — and nothing downstream puts them back: `ApplyToEntity` writes the mutation's
///         properties and knows nothing about the entity's defaults.
///     </para>
///     <para>
///         ⚠️ **The ladder has two rungs, not three.** A parameterless `Create()` is emitted for
///         **every** entity, so there is no case where the invoker falls back to `new`. The factory
///         with parameters cannot be named from here, because its signature would have to be
///         <em>predicted</em> by a feature that does not see it.
///     </para>
///     <para>
///         With the overload always present, the question «can I call `Create()`?» becomes «is it an
///         entity?», that is an attribute the author wrote — so there is no second copy of the rule
///         «what is required at creation» to keep in step.
///     </para>
/// </remarks>
public class EntityConstructionTests : ActionsGeneratorTestBase
{
    private const string EfCorePresence = """
        namespace Pragmatic.Persistence.EFCore
        {
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class PragmaticDbContextAttribute : Attribute { }
        }
        """;

    private static string Source(string extraProperty, string constructor = "") => $$"""
        using System;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Persistence.Entity;

        {{EfCorePresence}}

        namespace TestApp;

        public class SalesBoundary;

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class Order : IEntity
        {
            {{constructor}}
            public string Reference { get; private set; } = "";
            {{extraProperty}}
        }

        [Mutation(Mode = MutationMode.Create)]
        public partial class CreateOrderMutation : Mutation<Order>
        {
            public string Reference { get; init; } = "";
        }
        """;

    /// <summary>Without required properties: the factory is empty and the invoker uses it.</summary>
    [Fact]
    public void WithNothingRequired_TheInvokerUsesTheFactory()
    {
        var result = RunWithPersistence(Source(""));

        GetGeneratedSource(result, "Order.Create")!.Should().Contain("Create()");
        GetGeneratedSource(result, "CreateOrderMutation.MutationInvoker")!
            .Should().Contain("Order.Create()");
    }

    /// <summary>
    ///     ⚠️ And with a required property **too**: the case that would otherwise fall back to <c>new</c>.
    /// </summary>
    /// <remarks>
    ///     A <c>decimal</c> without an initializer is a factory parameter, and a non-nullable
    ///     <c>string</c> never is — it must have an initializer, or it is CS8618. That is why almost
    ///     every real entity has a factory with parameters, and why a fallback branch would cover
    ///     nearly every case.
    /// </remarks>
    [Fact]
    public void WithSomethingRequired_TheInvokerStillUsesTheFactory()
    {
        var result = RunWithPersistence(Source("public decimal Total { get; private set; }"));

        var entity = GetGeneratedSource(result, "Order.Create")!;
        entity.Should().Contain("Create(decimal total)", "the overload with the required members stays");
        entity.Should().Contain("Create()",
            "and next to it the empty one, which is what makes construction uniform");

        var invoker = GetGeneratedSource(result, "CreateOrderMutation.MutationInvoker")!;
        invoker.Should().Contain("Order.Create()");
        invoker.Should().NotContain("new global::TestApp.Order()",
            "there is no case where construction skips the entity's defaults");
    }

    /// <summary>
    ///     ⚠️ A <b>declared</b> constructor wins, and it is chosen by <c>ConstructorAnalyzer</c> —
    ///     Mapping's, not a second one.
    /// </summary>
    /// <remarks>
    ///     That is the difference that matters: the factory would have to be predicted, a declared
    ///     constructor is <b>seen</b>. So the arguments come from the parameters of a real symbol. And
    ///     the choice is the same one <c>ToEntity</c> applies, so the two paths cannot disagree on which
    ///     constructor is «the best».
    /// </remarks>
    [Fact]
    public void ADeclaredConstructor_IsChosenAndFedFromTheMutation()
    {
        var result = RunWithPersistence(
            Source("", "public Order(string reference) { Reference = reference; }"));

        var invoker = GetGeneratedSource(result, "CreateOrderMutation.MutationInvoker")!;

        // Named, not positional: it is what allows omitting an optional parameter the mutation does
        // not carry, so that one takes its own default value instead of `default`.
        invoker.Should().Contain("new global::TestApp.Order(reference: this.Reference)",
            "the declared constructor is the one the author wrote to build the entity");
        invoker.Should().NotContain("Order.Create()",
            "with a declared constructor the factory is not used: two doors to the same thing is "
            + "the shape this rule exists to avoid");
    }

    /// <summary>
    ///     With the references that also switch on the persistence generator: without them the factory
    ///     would not be emitted and the cases would compare the choice against nothing.
    /// </summary>
    private static SourceGenRunResult RunWithPersistence(string source)
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
