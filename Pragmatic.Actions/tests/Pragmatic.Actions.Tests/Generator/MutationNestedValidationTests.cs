using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     <c>ValidateNestedTree()</c> calls the child's own <c>Validate()</c> exactly when the child will
///     have one.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ Two answers to the same question. The validation generator emits a <c>Validate()</c>
///         for any type with a rule <b>or</b> a C# <c>required</c> member; the mutation transform
///         predicted "will this type have a Validate()?" by looking at attributes alone. A child that
///         validated for <c>required</c> alone had a <c>Validate()</c> nobody called from the tree —
///         and the parent's own validator does not loop over it either, for the same reason. Measured
///         by <c>TheChildThatValidatesForRequiredAlone</c> in <c>examples/conformance</c>.
///     </para>
///     <para>
///         The control is the second test: a child with nothing to validate must not have the call
///         emitted, because calling a <c>Validate()</c> that will not exist is a CS1061 inside a
///         generated file.
///     </para>
/// </remarks>
public class MutationNestedValidationTests : ActionsGeneratorTestBase
{
    private const string EfCorePresence = """
        namespace Pragmatic.Persistence.EFCore
        {
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class PragmaticDbContextAttribute : Attribute { }
        }
        """;

    /// <param name="childBody">The child mutation's members: what makes it validatable, or not.</param>
    private static string Source(string childBody) => $$"""
        using System;
        using System.Collections.Generic;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Validation.Attributes;

        {{EfCorePresence}}

        namespace TestApp;

        public class SalesBoundary;

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class Order : IEntity
        {
            public ICollection<LineItem> Lines { get; set; } = new List<LineItem>();
        }

        [Entity]
        [BelongsTo<SalesBoundary>]
        [PartOf<Order>]
        public partial class LineItem : IEntity
        {
            public string Description { get; set; } = "";
        }

        [Mutation(Mode = MutationMode.Update, Internal = true)]
        public partial class WriteLineItemMutation : Mutation<LineItem>
        {
            {{childBody}}
        }

        [Mutation(Mode = MutationMode.Update)]
        public partial class UpdateOrderMutation : Mutation<Order>
        {
            public Guid Id { get; init; }
            public List<WriteLineItemMutation> Lines { get; init; } = new();
        }
        """;

    [Fact]
    public void AChildThatValidatesForRequiredAlone_HasItsValidateCalled()
    {
        var result = RunGeneratorWithValidation(Source("""
            public Guid Id { get; init; }
            public required string Description { get; init; }
            """));

        var generated = GetGeneratedSource(result, "WriteLineItemMutation.ApplyToEntity");
        generated.Should().NotBeNull();
        generated!.Should().Contain("Validate()",
            "a required member gives the child a Validate(), and the tree must call it");
        NoErrorsIn(result, "WriteLineItemMutation.ApplyToEntity");
    }

    [Fact]
    public void TheControl_AChildWithNothingToValidate_IsNotCalled()
    {
        var result = RunGeneratorWithValidation(Source("""
            public Guid Id { get; init; }
            public string Description { get; init; } = "";
            """));

        var generated = GetGeneratedSource(result, "WriteLineItemMutation.ApplyToEntity");
        generated.Should().NotBeNull();
        generated!.Should().NotContain("Validate()",
            "no rule and no required member: there is no Validate() to call, and naming one is CS1061");
        NoErrorsIn(result, "WriteLineItemMutation.ApplyToEntity");
    }

    /// <summary>
    ///     Errors in the file under test only: the fixture references no EF Core provider, so the
    ///     generated repositories do not compile, and that is not what these cases measure.
    /// </summary>
    private static void NoErrorsIn(SourceGenRunResult result, string hint)
    {
        var errors = GeneratorTestHelper.GetCompilationErrors(result)
            .Where(d => d.Location.SourceTree?.FilePath.Contains(hint) == true)
            .ToList();

        errors.Should().BeEmpty(string.Join(Environment.NewLine, errors.Select(d => d.ToString())));
    }

    /// <summary>
    ///     With Pragmatic.Validation referenced: without it the tree is not emitted at all, because
    ///     the generated code names <c>ValidationError</c>.
    /// </summary>
    private static SourceGenRunResult RunGeneratorWithValidation(string source)
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
                GeneratorTestHelper.FromType<Pragmatic.Specification.Specification<object>>(),
                GeneratorTestHelper.FromType<Pragmatic.Validation.Attributes.ValidationAttribute>(),
                GeneratorTestHelper.FromType<Microsoft.EntityFrameworkCore.DbContext>(),
                GeneratorTestHelper.FromType<Microsoft.Extensions.DependencyInjection.IServiceCollection>(),
                GeneratorTestHelper.FromTypeAssembly(
                    typeof(Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions)),
                GeneratorTestHelper.FromType<Microsoft.Extensions.Logging.ILogger>(),
            ]);
}
