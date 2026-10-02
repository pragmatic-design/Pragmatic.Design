using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.SourceGen;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Core;

/// <summary>
///     Every constant in <see cref="AttributeNames" /> names a type that exists.
/// </summary>
/// <remarks>
///     <para>
///         A constant handed to <c>ForAttributeWithMetadataName</c> or <c>GetTypeByMetadataName</c> that
///         names no type matches nothing and says nothing: the feature behind it never runs, and the build
///         is green. The wrong name is an easy one to write —
///         <c>Pragmatic.Messaging.Attributes.RetryAttribute</c> where the attribute lives in
///         <c>Pragmatic.Resilience.Attributes</c>, a <c>Pragmatic.Localization</c> namespace that does not
///         exist — and an unused constant hides it until the day something uses it.
///     </para>
///     <para>
///         The lookup is the generator's own: a compilation over every Pragmatic runtime assembly this
///         suite ships with, asked by metadata name. A module whose attributes appear here and is not yet
///         referenced by this project fails this test until it is — which is the point.
///     </para>
/// </remarks>
public class AttributeNamesResolveTests
{
    [Fact]
    public void EveryConstant_NamesATypeThatExists()
    {
        var compilation = CSharpCompilation.Create("AttributeNamesProbe", references: RuntimeAssemblies());

        var unresolved = Constants()
            .Where(c => compilation.GetTypesByMetadataName(c.Value).IsEmpty)
            .Select(c => $"{c.Name} = {c.Value}")
            .ToList();

        unresolved.Should().BeEmpty(
            "a metadata name that resolves to nothing turns the feature reading it off in silence: "
            + string.Join(" | ", unresolved));
    }

    /// <summary>
    ///     The control: the lookup does find a type when there is one, and does not when there is not.
    /// </summary>
    /// <remarks>
    ///     Without it the test above would also pass on a compilation that resolved everything — or
    ///     fail on one that resolved nothing — for reasons that have nothing to do with the file.
    /// </remarks>
    [Fact]
    public void TheLookup_FindsARealAttribute_AndNotAnInventedOne()
    {
        var compilation = CSharpCompilation.Create("AttributeNamesProbe", references: RuntimeAssemblies());

        compilation.GetTypesByMetadataName(AttributeNames.MapFrom).Should().ContainSingle();
        compilation.GetTypesByMetadataName("Pragmatic.Localization.LocalizeAttribute").Should().BeEmpty();
    }

    private static IEnumerable<(string Name, string Value)> Constants()
        => typeof(AttributeNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (f.Name, (string)f.GetRawConstantValue()!));

    /// <summary>
    ///     The Pragmatic runtime assemblies next to this suite. Generators and test assemblies are left
    ///     out: they declare no attribute a consumer writes.
    /// </summary>
    private static List<MetadataReference> RuntimeAssemblies()
        => Directory.GetFiles(AppContext.BaseDirectory, "Pragmatic.*.dll")
            .Where(path =>
            {
                var name = Path.GetFileNameWithoutExtension(path);
                return !name.Contains("SourceGen", StringComparison.Ordinal)
                       && !name.EndsWith(".Tests", StringComparison.Ordinal)
                       && !name.Contains("Analyzers", StringComparison.Ordinal)
                       && !name.Contains("CodeFixers", StringComparison.Ordinal);
            })
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
}
