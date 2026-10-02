using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.FastEnum;

/// <summary>
///     A generated converter that nobody registers does nothing. This pins the wiring:
///     a module emits a registration entry point plus the metadata attribute that advertises it, and
///     the host calls it while configuring the HTTP JSON options — ahead of JsonStringEnumConverter,
///     which accepts every enum and would otherwise shadow it.
/// </summary>
public class FastEnumConverterWiringTests
{
    // The real attribute, from Pragmatic.Abstractions — the same assembly that carries
    // [PragmaticMetadata], which the metadata artifact needs in order to compile.
    private const string ModuleSource = """
        namespace Sample.Billing
        {
            [Pragmatic.FastEnum]
            public enum InvoiceStatus { Draft, Paid }
        }

        namespace Sample.Booking
        {
            [Pragmatic.FastEnum]
            public enum ReservationStatus { Pending, Confirmed }
        }
        """;

    private static Dictionary<string, string> RunModule(string source)
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            source,
            GeneratorTestHelper.FromTypeAssembly(typeof(JsonConverter<int>)),
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Composition.Metadata.MetadataCategory)));

        GeneratorTestHelper.HasCompilationErrors(result).Should().BeFalse();
        return GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
    }

    [Fact]
    public void Module_WithFastEnums_EmitsOneRegistrationCoveringEveryConverter()
    {
        var generated = RunModule(ModuleSource);

        generated.Should().ContainKey("_Infra.FastEnum.Registration.g.cs");

        // Whole lines, not substrings: "class X" also matches a declaration of "class XSomethingElse".
        var registration = Lines(generated["_Infra.FastEnum.Registration.g.cs"]);
        registration.Should().Contain("public static class PragmaticFastEnumJsonConverters");
        registration.Should().Contain("public static void AddTo(global::System.Text.Json.JsonSerializerOptions options)");
        registration.Should().Contain("options.Converters.Add(new global::Sample.Billing.InvoiceStatusJsonConverter());");
        registration.Should().Contain("options.Converters.Add(new global::Sample.Booking.ReservationStatusJsonConverter());");
    }

    [Fact]
    public void Module_WithFastEnums_AdvertisesTheRegistrationThroughAssemblyMetadata()
    {
        var generated = RunModule(ModuleSource);

        generated.Should().ContainKey("_Metadata.FastEnumConverters.g.cs");

        var metadata = generated["_Metadata.FastEnumConverters.g.cs"];
        // 24, not 23: 23 is PersonalData. Two categories sharing a number make the payloads
        // indistinguishable to the host reader.
        metadata.Should().Contain("(MetadataCategory)24");
        metadata.Should().Contain("TestAssembly.Generated.PragmaticFastEnumJsonConverters.AddTo");
    }

    [Fact]
    public void Module_AdvertisedRegistrationMethod_IsTheOneTheRegistrationFileActuallyDeclares()
    {
        // Two templates build this name independently: the metadata advertises it, the registration
        // declares it. The host emits a call to whatever the metadata says, so if they ever drift the
        // host stops compiling — against a method that no test would otherwise have compared.
        var generated = RunModule(ModuleSource);

        var advertised = RegistrationMethodFromMetadata(generated["_Metadata.FastEnumConverters.g.cs"]);
        var lastDot = advertised.LastIndexOf('.');
        var declaringType = advertised.Substring(0, lastDot);
        var methodName = advertised.Substring(lastDot + 1);

        var typeLastDot = declaringType.LastIndexOf('.');
        var declaringNamespace = declaringType.Substring(0, typeLastDot);
        var className = declaringType.Substring(typeLastDot + 1);

        var registration = Lines(generated["_Infra.FastEnum.Registration.g.cs"]);
        registration.Should().Contain($"namespace {declaringNamespace};");
        registration.Should().Contain($"public static class {className}");
        registration.Should().Contain(l => l.StartsWith($"public static void {methodName}(", StringComparison.Ordinal));
    }

    /// <summary>Trimmed source lines — lets an assertion match a whole declaration, not a substring of one.</summary>
    private static List<string> Lines(string source)
        => source.Split('\n').Select(l => l.Trim()).ToList();

    /// <summary>Pulls the advertised registrationMethod out of the emitted metadata attribute.</summary>
    private static string RegistrationMethodFromMetadata(string metadataSource)
    {
        const string key = "\"registrationMethod\": \"";
        var start = metadataSource.IndexOf(key, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "the metadata must advertise a registrationMethod");
        start += key.Length;
        var end = metadataSource.IndexOf('"', start);
        return metadataSource.Substring(start, end - start);
    }

    [Fact]
    public void Module_WithoutFastEnums_EmitsNoRegistration()
    {
        var generated = RunModule("""
            namespace Sample
            {
                public enum Plain { A, B }
            }
            """);

        generated.Should().NotContainKey("_Infra.FastEnum.Registration.g.cs");
        generated.Should().NotContainKey("_Metadata.FastEnumConverters.g.cs");
    }

    [Fact]
    public void Host_CallsTheModuleRegistration_BeforeTheFrameworkEnumConverter()
    {
        var entry = RunHost(ModuleAssembly("App.Billing", "App.Billing.Generated.PragmaticFastEnumJsonConverters.AddTo"));

        entry.Should().Contain(
            "global::App.Billing.Generated.PragmaticFastEnumJsonConverters.AddTo(jsonOptions.SerializerOptions);");

        // Converters is consulted front to back and JsonStringEnumConverter accepts every enum, so a
        // registration emitted after it would never run. Order is the contract, not a detail.
        var fastEnumAt = entry.IndexOf("PragmaticFastEnumJsonConverters.AddTo", StringComparison.Ordinal);
        var frameworkAt = entry.IndexOf("JsonStringEnumConverter", StringComparison.Ordinal);
        frameworkAt.Should().BeGreaterThan(fastEnumAt);
    }

    [Fact]
    public void Host_WithoutAnyFastEnumModule_KeepsTheFrameworkConverterAlone()
    {
        var entry = RunHost();

        entry.Should().Contain("JsonStringEnumConverter");
        entry.Should().NotContain("PragmaticFastEnumJsonConverters");
    }

    // ---------------------------------------------------------------------------------------------
    // Host-mode harness: the host must see the module only through a reference, the way it does in a
    // real build — the metadata attribute is read from compiled metadata, not from source.
    // ---------------------------------------------------------------------------------------------

    private const string CompositionShims = """
        namespace Pragmatic.Composition.Metadata
        {
            public enum MetadataCategory { DI = 0, PersonalData = 23, FastEnumConverters = 24 }
        }
        namespace Pragmatic.Composition.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = true)]
            public sealed class PragmaticMetadataAttribute : System.Attribute
            {
                public PragmaticMetadataAttribute(
                    Pragmatic.Composition.Metadata.MetadataCategory category, string schemaVersion, string jsonData)
                {
                    Category = category;
                    SchemaVersion = schemaVersion;
                    JsonData = jsonData;
                }

                public Pragmatic.Composition.Metadata.MetadataCategory Category { get; }
                public string SchemaVersion { get; }
                public string JsonData { get; }
            }
        }
        namespace Pragmatic.Composition.Hosting
        {
            // Presence of this type is what makes CompositionDetector treat the exe as a HOST.
            public class PragmaticBuilder { }
        }
        """;

    private const string HostSource = """
        namespace MyHost
        {
            public static class Program { public static void Main() { } }
        }
        """;

    private static MetadataReference? _contracts;

    /// <summary>
    ///     One shared contracts assembly: <c>GetTypeByMetadataName</c> returns null on an ambiguous
    ///     metadata name, so duplicating the shims per module would silently switch Composition off.
    /// </summary>
    private static MetadataReference Contracts => _contracts ??= Emit("Pragmatic.Composition", CompositionShims);

    private static string RunHost(params MetadataReference[] modules)
    {
        var result = GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(
            HostSource, [Contracts, .. modules]);

        var generated = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        generated.Should().ContainKey("Host.Entry.g.cs");
        return generated["Host.Entry.g.cs"];
    }

    /// <summary>Compiles a boundary-library assembly advertising a converter registration.</summary>
    private static MetadataReference ModuleAssembly(string assemblyName, string registrationMethod)
    {
        var json = $$"""{"generator":"Pragmatic.SourceGenerator/FastEnum","registrationMethod":"{{registrationMethod}}"}""";
        var source = $$"""
            [assembly: Pragmatic.Composition.Attributes.PragmaticMetadata(
                (Pragmatic.Composition.Metadata.MetadataCategory)24, "1.0", {{SymbolDisplay.FormatLiteral(json, quote: true)}})]

            namespace {{registrationMethod.Substring(0, registrationMethod.LastIndexOf('.'))}}
            {
                public static class Placeholder { }
            }
            """;

        return Emit(assemblyName, source, Contracts);
    }

    private static MetadataReference Emit(string assemblyName, string source, params MetadataReference[] extra)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            BaseReferences.Concat(extra),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        emit.Success.Should().BeTrue(string.Join("; ",
            emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static MetadataReference[] BaseReferences =>
    [
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(Path.Combine(
            Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll"))
    ];
}
