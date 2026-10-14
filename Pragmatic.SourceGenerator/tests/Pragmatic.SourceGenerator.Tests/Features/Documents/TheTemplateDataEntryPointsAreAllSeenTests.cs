using System.Reflection;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.SourceGenerator.Features.Documents.Transforms;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.SourceGenerator.Tests.Features.Documents;

/// <summary>
/// The generator asks the semantic model about a call only when its name is one of the entry points it
/// knows, so a member the runtime marks <c>[TemplateData]</c> under a new name would be passed over in
/// silence and its type read by reflection. This keeps the two lists one.
/// </summary>
public class TheTemplateDataEntryPointsAreAllSeenTests
{
    [Fact]
    public void EveryMarkedMember_IsAnEntryPointTheGeneratorLooksFor()
    {
        var marked = MarkedMemberNames(typeof(TemplateDataAttribute).Assembly);

        marked.Should().NotBeEmpty();
        marked.Except(TemplateDataTypeReader.EntryPointNames).Should().BeEmpty();
    }

    [Fact]
    public void EveryEntryPointTheGeneratorLooksFor_IsMarked()
    {
        var marked = MarkedMemberNames(typeof(TemplateDataAttribute).Assembly);

        TemplateDataTypeReader.EntryPointNames.Except(marked).Should().BeEmpty();
    }

    /// <summary>
    /// Methods by name, types by their name without the arity suffix. An extension member is compiled
    /// into a static method of the same name on the containing class, so it is found as a method.
    /// </summary>
    private static HashSet<string> MarkedMemberNames(Assembly assembly)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in assembly.GetExportedTypes())
        {
            if (type.IsGenericTypeDefinition && type.GetGenericArguments().Any(IsMarked))
                names.Add(type.Name[..type.Name.IndexOf('`')]);

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (method.IsGenericMethodDefinition && method.GetGenericArguments().Any(IsMarked))
                    names.Add(method.Name);
            }
        }

        return names;
    }

    private static bool IsMarked(Type parameter) => parameter.IsDefined(typeof(TemplateDataAttribute), inherit: false);
}
