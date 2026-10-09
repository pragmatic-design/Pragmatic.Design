using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Serialization.Analysis;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.SourceGenerator.Tests.Features.Serialization;

/// <summary>
///     A source with a type, the JSON context the generator emits for it, compiled together and loaded, so a test
///     can write a value through the context and through reflection under the same options and compare the bytes.
/// </summary>
/// <remarks>
///     The source declares <c>public static class Samples</c> with static methods returning the values to write, as
///     <see cref="CompiledResponseWriter" /> does.
/// </remarks>
internal sealed class CompiledJsonContext
{
    private const string AssemblyName = "JsonContextTest";
    private readonly Assembly _assembly;
    private readonly string _namespace;

    private CompiledJsonContext(Assembly assembly, string @namespace, string generated)
    {
        _assembly = assembly;
        _namespace = @namespace;
        Generated = generated;
    }

    /// <summary>The generated context file.</summary>
    public string Generated { get; }

    /// <summary>Extracts the closure of the type <paramref name="metadataName" /> names, renders its context and compiles it.</summary>
    public static CompiledJsonContext For(string source, string metadataName)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            AssemblyName, [tree], References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();

        var root = compilation.GetTypeByMetadataName(metadataName)!;
        JsonShapeExtractor.TryExtractClosure(root, out var objects, out var leaves, out var collections)
            .Should().BeTrue($"the generator must be able to describe {metadataName}");

        var @namespace = root.ContainingNamespace.ToDisplayString();
        var generated = new PragmaticJsonContextTemplate(new JsonContextModel(@namespace, objects, leaves, collections), "Context.g.cs")
            .ToSourceText().ToString();

        using var image = new MemoryStream();
        var emitted = compilation
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(generated, new CSharpParseOptions(LanguageVersion.Preview)))
            .Emit(image);
        emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString())
            .Should().BeEmpty(generated);

        var assembly = new AssemblyLoadContext(null, isCollectible: true).LoadFromStream(new MemoryStream(image.ToArray()));
        return new CompiledJsonContext(assembly, @namespace, generated);
    }

    /// <summary>A sample the source declares on its <c>Samples</c> class.</summary>
    public object Sample(string method)
        => _assembly.GetTypes().Single(t => t.Name == "Samples")
            .GetMethod(method, BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null)!;

    /// <summary>What the generated context writes for the value under the host's options.</summary>
    public string WriteThroughTheContext(object value)
    {
        var context = (IJsonTypeInfoResolver)_assembly
            .GetType(_namespace + ".Generated.PragmaticJsonContext", throwOnError: true)!
            .GetProperty("Default", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;

        return JsonSerializer.Serialize(value, value.GetType(), HostOptions(context));
    }

    /// <summary>What the reflection resolver writes for the value under the same options.</summary>
    public static string WriteThroughReflection(object value)
        => JsonSerializer.Serialize(value, value.GetType(), HostOptions(new DefaultJsonTypeInfoResolver()));

    // The options the host sets (as CompiledResponseWriter states them): camelCase, nulls left out, enums by name.
    private static JsonSerializerOptions HostOptions(IJsonTypeInfoResolver resolver)
    {
        var options = new Microsoft.AspNetCore.Http.Json.JsonOptions().SerializerOptions;
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.TypeInfoResolver = resolver;
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static IEnumerable<MetadataReference> References()
        => ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
}
