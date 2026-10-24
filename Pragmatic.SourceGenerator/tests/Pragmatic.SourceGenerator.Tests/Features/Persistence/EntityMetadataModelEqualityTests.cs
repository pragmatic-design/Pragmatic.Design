using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     Two entity models are equal only when everything the generator reads from them is.
/// </summary>
/// <remarks>
///     <see cref="EntityMetadataModel.Equals(EntityMetadataModel)" /> is written by hand, field by field, and the
///     incremental pipeline trusts it: a model that compares equal to the previous run's is not handed to any
///     template again. A field left out of it is a fact the editor stops seeing change, so its generated code stays
///     as it was until the next full build.
/// </remarks>
public class EntityMetadataModelEqualityTests
{
    /// <summary>
    ///     Excluded on purpose: a position never changes the generated output (see the property's own remarks).
    /// </summary>
    private static readonly HashSet<string> NotCompared = [nameof(EntityMetadataModel.DeclarationLocation)];

    public static TheoryData<string> SettableProperties()
    {
        var data = new TheoryData<string>();
        foreach (var property in typeof(EntityMetadataModel).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.SetMethod is not null && !NotCompared.Contains(property.Name))
                data.Add(property.Name);
        }

        return data;
    }

    /// <summary>Every field the model carries, changed alone, makes the model a different one.</summary>
    /// <remarks>
    ///     Read off the type, so a field added later is checked without anybody remembering to list it here.
    /// </remarks>
    [Theory]
    [MemberData(nameof(SettableProperties))]
    public void AModelThatDiffersInOneField_IsNotEqual(string name)
    {
        var property = typeof(EntityMetadataModel).GetProperty(name)!;
        var original = Model();
        var changed = original with { };
        property.SetValue(changed, ADifferentValue(property.PropertyType, property.GetValue(original)));

        changed.Equals(original).Should().BeFalse(
            $"{name} differs, and a model that compares equal is not regenerated");
    }

    /// <summary>The control: a copy with nothing changed is equal, so the test above is about the field.</summary>
    [Fact]
    public void AnUnchangedCopy_IsEqual()
    {
        var original = Model();

        (original with { }).Equals(original).Should().BeTrue();
    }

    /// <summary>
    ///     The effect in an editor session: a rule added to an entity reaches the metadata the host reads on the next
    ///     run of the same generator driver.
    /// </summary>
    [Fact]
    public void ARuleAddedBetweenTwoRuns_ReachesTheModulesMetadata()
    {
        const string rule = """
            namespace Catalog;

            public sealed class ActiveOnly : Pragmatic.Persistence.Query.Filters.VisibilityRule<Property>
            {
                public override System.Linq.Expressions.Expression<System.Func<Property, bool>> ToExpression() => p => p.Name != "";
            }
            """;
        string Entity(string attribute) => $$"""
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Persistence.Entity;

            namespace Catalog;

            [Boundary]
            public partial class CatalogBoundary;

            [Entity]
            {{attribute}}
            public partial class Property : IEntity
            {
                public string Name { get; private set; } = "";
            }
            """;

        var before = CSharpSyntaxTree.ParseText(Entity(""), path: "Property.cs");
        var compilation = CSharpCompilation.Create(
            "Catalog.Module",
            [before, CSharpSyntaxTree.ParseText(rule, path: "ActiveOnly.cs")],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new PragmaticSourceGenerator());
        driver = driver.RunGenerators(compilation);
        MetadataOf(driver).Should().NotContain("visibilityRules", "the entity names no rule yet");

        var after = CSharpSyntaxTree.ParseText(Entity("[VisibleWhen<ActiveOnly>]"), path: "Property.cs");
        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(before, after));

        MetadataOf(driver).Should().Contain("visibilityRules",
            "the rule was added, and the host installs only the rules the module's metadata names");
    }

    private static string MetadataOf(GeneratorDriver driver)
    {
        var metadata = driver.GetRunResult().Results.Single().GeneratedSources
            .Where(s => s.HintName.Contains("_Metadata.Persistence"))
            .Select(s => s.SourceText.ToString())
            .SingleOrDefault();
        metadata.Should().NotBeNull("the module has an entity, so it publishes persistence metadata");
        return metadata!;
    }

    private static EntityMetadataModel Model() => new()
    {
        TypeName = "Property",
        FullTypeName = "Catalog.Property",
        Namespace = "Catalog",
        IdType = "Guid"
    };

    /// <summary>A value of <paramref name="type" /> that differs from <paramref name="current" />.</summary>
    private static object? ADifferentValue(Type type, object? current)
    {
        if (type == typeof(bool))
            return !(bool)current!;
        if (type == typeof(int))
            return (int)current! + 1;
        if (type == typeof(string))
            return (string?)current + "changed";
        if (type == typeof(TraitParentModel))
            return new TraitParentModel(((TraitParentModel?)current)?.TypeName + "Changed", true, false);

        // EquatableArray<T>: one element where there were none. Equality compares the lengths first, so the
        // element's own contents are never read and an uninitialised one is enough.
        if (type.IsGenericType && type.GetGenericTypeDefinition().Name == "EquatableArray`1")
        {
            var elementType = type.GetGenericArguments()[0];
            var element = elementType == typeof(string) ? "changed" : RuntimeHelpers.GetUninitializedObject(elementType);
            var create = typeof(ImmutableArray).GetMethods()
                .Single(m => m is { Name: nameof(ImmutableArray.Create), IsGenericMethod: true }
                             && m.GetParameters() is [{ ParameterType.IsGenericParameter: true }])
                .MakeGenericMethod(elementType);
            return Activator.CreateInstance(type, create.Invoke(null, [element]));
        }

        throw new InvalidOperationException(
            $"No different value for a field of type {type}: teach this test the type, so the field is checked.");
    }

    private static readonly MetadataReference[] References =
    [
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(System.Linq.Expressions.Expression).Assembly.Location),
        MetadataReference.CreateFromFile(Path.Combine(
            Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll")),
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Persistence.Query.Filters.VisibilityRule<object>>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Actions.Attributes.BoundaryAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.MultiTenancy.ITenantEntity>()
    ];
}
