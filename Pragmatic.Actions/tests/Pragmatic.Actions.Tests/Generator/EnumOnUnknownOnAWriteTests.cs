using System.Linq;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     Two pieces come out of one declaration, and they have to agree about the same input.
/// </summary>
/// <remarks>
///     <para>
///         The mapping honours <c>[MapEnum(OnUnknown = …)]</c> — <c>TryParse(...) ? value : default</c>.
///         The mutation's conversion guard refuses the same string with <c>validation.conversion</c>,
///         and it runs first, in <c>ValidateNestedTree</c>. So a name the enum does not have was a
///         <b>422</b> and the <c>: default</c> branch was unreachable from any request: a member of a
///         public enum that nothing could execute.
///     </para>
///     <para>
///         The reading taken here, and it comes from the attribute's own stated purpose: a declared
///         <c>OnUnknown</c> <b>is</b> the author's answer to "what happens when the name matches
///         nothing", so the guard has nothing left to decide for that property. Where it is left at
///         <c>Throw</c> — the default — the guard stays, and it is an improvement on what it replaced:
///         a 422 for a wrong body instead of an exception surfacing as a 500.
///     </para>
/// </remarks>
public class EnumOnUnknownOnAWriteTests : ActionsGeneratorTestBase
{
    private const string EfCorePresence = """
        namespace Pragmatic.Persistence.EFCore
        {
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class PragmaticDbContextAttribute : Attribute { }
        }
        """;

    private static string Source(string mutationProperty) => $$"""
        using System;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Mapping;
        using Pragmatic.Mapping.Attributes;
        using Pragmatic.Persistence.Entity;

        {{EfCorePresence}}

        namespace TestApp;

        public class SalesBoundary;

        public enum Channel { Unknown = 0, Web = 1, Phone = 2 }

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class Order : IEntity
        {
            public Channel Channel { get; set; }
            public int Quantity { get; set; }
        }

        [Mutation(Mode = MutationMode.Update)]
        public partial class UpdateOrderMutation : Mutation<Order>
        {
            public Guid Id { get; init; }
            public string? Quantity { get; init; }
        {{mutationProperty}}
        }
        """;

    /// <summary>Everything the generator wrote for this mutation, joined.</summary>
    /// <remarks>
    ///     The guard and the mapping body land in different files, and the point of these tests is
    ///     that the two agree — so the subject is both of them at once.
    /// </remarks>
    private static string EverythingGeneratedForTheMutation(string mutationProperty)
    {
        var result = RunGeneratorForMutation(Source(mutationProperty));

        var files = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Where(kv => kv.Key.Contains("UpdateOrderMutation"))
            .Select(kv => kv.Value)
            .ToList();

        files.Should().NotBeEmpty("the mutation generates at all");

        return string.Join(System.Environment.NewLine, files);
    }

    private static SourceGenRunResult RunGeneratorForMutation(string source)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            source,
            [
                GeneratorTestHelper.FromType<Pragmatic.Actions.Attributes.DomainActionAttribute>(),
                GeneratorTestHelper.FromType<Pragmatic.Result.IError>(),
                GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Result.Result<,>)),
                GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Ensure.Ensure)),
                GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.IEntity>(),
                GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.SoftDeleteAttribute>(),
                GeneratorTestHelper.FromType<Pragmatic.Mapping.Attributes.MapToAttribute<object>>(),
                GeneratorTestHelper.FromType<Pragmatic.Validation.ISyncValidator>(),
                GeneratorTestHelper.FromType<Pragmatic.Specification.Specification<object>>(),
                GeneratorTestHelper.FromType<Microsoft.EntityFrameworkCore.DbContext>(),
                GeneratorTestHelper.FromType<Microsoft.Extensions.DependencyInjection.IServiceCollection>(),
                GeneratorTestHelper.FromTypeAssembly(
                    typeof(Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions)),
                GeneratorTestHelper.FromType<Microsoft.Extensions.Logging.ILogger>(),
            ]);

    /// <summary>A declared <c>OnUnknown</c> decides, so the guard does not pre-empt it.</summary>
    [Fact]
    public void ADeclaredOnUnknown_IsNotOverruledByTheConversionGuard()
    {
        var generated = EverythingGeneratedForTheMutation("""
                [MapEnum(OnUnknown = UnknownEnumValue.Default)]
                public string? Channel { get; init; }
            """);

        generated.Should().NotContain("nameof(Channel), \"validation.conversion\"",
            "the author declared what an unrecognised name becomes; refusing it first makes the "
            + "declaration unreachable");
    }

    /// <summary>
    ///     The control: left at <c>Throw</c>, the guard still refuses.
    /// </summary>
    /// <remarks>
    ///     Without it, "no conversion error for Channel" would be satisfied by deleting the guard —
    ///     which is rule 4.6 gone, and an unparseable enum name back to surfacing as a 500.
    /// </remarks>
    [Fact]
    public void WithoutADeclaredOnUnknown_TheGuardStillRefuses()
    {
        var generated = EverythingGeneratedForTheMutation("""
                public string? Channel { get; init; }
            """);

        generated.Should().Contain("nameof(Channel), \"validation.conversion\"",
            "an undeclared enum conversion is still refused rather than zeroed");
    }

    /// <summary>
    ///     The second control: the guard on a neighbouring property is untouched either way.
    /// </summary>
    /// <remarks>
    ///     It keeps the change property-local. Reading the attribute and then dropping every guard on
    ///     the mutation would satisfy the first assertion and quietly take rule 4.6 with it.
    /// </remarks>
    [Fact]
    public void TheGuardOnAnotherProperty_IsUntouched()
    {
        var generated = EverythingGeneratedForTheMutation("""
                [MapEnum(OnUnknown = UnknownEnumValue.Default)]
                public string? Channel { get; init; }
            """);

        generated.Should().Contain("nameof(Quantity), \"validation.conversion\"",
            "a string that must become a number is still refused when it is not one");
    }
}
