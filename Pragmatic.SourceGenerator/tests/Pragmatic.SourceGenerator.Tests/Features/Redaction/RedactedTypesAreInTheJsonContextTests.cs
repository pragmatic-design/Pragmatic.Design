using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Redaction;

/// <summary>
///     A type the redaction map carries is a root of the generated JSON context, and the map hands that
///     context to the redactor.
/// </summary>
/// <remarks>
///     <para>
///         The redactor serializes a value before masking it. With reflection only, a host published
///         Native AOT lost every entry that carried a declared type: the serializer threw inside the
///         logger. The effect is proven where it happens, by <c>examples/aot-smoke</c> sample 4; these
///         are the generator-side facts it rests on.
///     </para>
///     <para>
///         The control is the assembly that does not opt in: no context, so the map must not name one.
///     </para>
/// </remarks>
public class RedactedTypesAreInTheJsonContextTests
{
    private const string Stubs = """
        namespace Pragmatic
        {
            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class NotLoggedAttribute : System.Attribute { }
        }
        namespace Pragmatic.Privacy
        {
            public enum DataCategory { Identity = 0, Contact = 1 }
            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class PersonalDataAttribute(DataCategory category) : System.Attribute
            {
                public DataCategory Category { get; } = category;
            }
        }
        namespace Pragmatic.Serialization
        {
            public sealed class PragmaticJsonOptions { }
            [System.AttributeUsage(System.AttributeTargets.Assembly)]
            public sealed class PragmaticGenerateJsonContextAttribute : System.Attribute { }
            public enum RedactionReason { NotLogged = 0, PersonalData = 1 }
            public readonly record struct RedactedMember(string Name, RedactionReason Reason, string? Category = null);
            public interface IRedactionMap
            {
                bool TryGetRedactedMembers(System.Type type, out System.Collections.Generic.IReadOnlyList<RedactedMember> members);
            }
        }
        namespace Microsoft.Extensions.DependencyInjection.Extensions
        {
            public static class ServiceCollectionDescriptorExtensions { }
        }
        namespace Sample.Crm
        {
            public sealed class Customer
            {
                public string Reference { get; set; } = "";
                [Pragmatic.Privacy.PersonalData(Pragmatic.Privacy.DataCategory.Contact)]
                public string Email { get; set; } = "";
            }
        }
        """;

    private static IReadOnlyDictionary<string, string> Generate(bool optIn)
    {
        var source = (optIn ? "[assembly: Pragmatic.Serialization.PragmaticGenerateJsonContext]\n" : "") + Stubs;
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);
        return GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
    }

    private static string Find(IReadOnlyDictionary<string, string> generated, string hint)
        => generated.Where(kv => kv.Key.Contains(hint, StringComparison.Ordinal)).Select(kv => kv.Value).FirstOrDefault() ?? "";

    [Fact]
    public void OptedIn_TheRedactedTypeIsARootOfTheJsonContext()
    {
        var context = Find(Generate(optIn: true), "Json.Context");

        context.Should().Contain("Create_Sample_Crm_Customer")
            .And.Contain("\"email\"")
            .And.Contain("\"reference\"");
    }

    [Fact]
    public void OptedIn_TheMapHandsTheContextToTheRedactor()
    {
        var generated = Generate(optIn: true);
        var map = Find(generated, "RedactionMap");
        var context = Find(generated, "Json.Context");

        map.Should().Contain("TypeInfoResolver => global::TestAssembly.Generated.PragmaticJsonContext.Default");
        // The class the map names is the one the serialization feature wrote, in the same namespace.
        context.Should().Contain("namespace TestAssembly.Generated;").And.Contain("class PragmaticJsonContext");
    }

    [Fact]
    public void NotOptedIn_TheMapNamesNoContext()
    {
        var generated = Generate(optIn: false);

        Find(generated, "RedactionMap").Should().Contain("Members0").And.NotContain("TypeInfoResolver");
        Find(generated, "Json.Context").Should().BeEmpty();
    }
}
