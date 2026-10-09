using System.Buffers;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.SourceGenerator.Features.Serialization.Analysis;
using Pragmatic.SourceGenerator.Features.Serialization.Models;
using Pragmatic.SourceGenerator.Features.Serialization.Templates;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.SourceGenerator.Tests.Features.Serialization;

/// <summary>
///     A source with a response type, its writer planned and generated, compiled together and loaded, so a test can
///     write a value through the writer and through the serializer and compare the bytes.
/// </summary>
/// <remarks>
///     The source declares <c>public static class Samples</c> with static methods returning the values to write: the
///     values are compiled C#, so a test states them as an author would.
/// </remarks>
internal sealed class CompiledResponseWriter
{
    private const string AssemblyName = "ResponseWriterTest";
    private readonly Assembly? _assembly;

    private CompiledResponseWriter(JsonResponseWriterPlan? plan, string? rejection, string generated, Assembly? assembly)
    {
        Plan = plan;
        Rejection = rejection;
        Generated = generated;
        _assembly = assembly;
    }

    /// <summary>The plan, or null when the type was refused.</summary>
    public JsonResponseWriterPlan? Plan { get; }

    /// <summary>Why the type was refused, or null.</summary>
    public string? Rejection { get; }

    /// <summary>The generated writer file, empty when the type was refused.</summary>
    public string Generated { get; }

    /// <summary>Plans, generates and compiles the writer of the type <paramref name="root" /> names in the source.</summary>
    public static CompiledResponseWriter For(string source, Func<Compilation, ITypeSymbol> root)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            AssemblyName, [tree], References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();

        var plan = JsonResponseWriterPlanner.TryPlan(root(compilation), compilation, out var rejection);
        if (plan is null)
            return new CompiledResponseWriter(null, rejection, "", null);

        var generated = new Utf8JsonWritersTemplate(AssemblyName + ".Generated", plan.Methods, JsonWriterProfile.Response)
            .ToSourceText().ToString();
        var withWriter = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(generated, new CSharpParseOptions(LanguageVersion.Preview)));

        using var image = new MemoryStream();
        var emitted = withWriter.Emit(image);
        emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString())
            .Should().BeEmpty(generated);

        var assembly = new AssemblyLoadContext(null, isCollectible: true).LoadFromStream(new MemoryStream(image.ToArray()));
        return new CompiledResponseWriter(plan, null, generated, assembly);
    }

    /// <summary>The type a metadata name names in the source.</summary>
    public static Func<Compilation, ITypeSymbol> Named(string metadataName)
        => compilation => compilation.GetTypeByMetadataName(metadataName)!;

    /// <summary>A sample the source declares on its <c>Samples</c> class, whatever namespace it is in.</summary>
    public object Sample(string method)
        => _assembly!.GetTypes().Single(t => t.Name == "Samples")
            .GetMethod(method, BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null)!;

    /// <summary>What the generated writer writes for the value.</summary>
    /// <remarks>
    ///     With the options <c>GeneratedJsonResponse</c> writes with, validation skipped included: a run of members
    ///     starts with a name where the writer expects one. The bytes are compared with the serializer's, which is the
    ///     check validation would have been.
    /// </remarks>
    public string Write(object value)
    {
        var writers = _assembly!.GetType(AssemblyName + ".Generated." + Utf8JsonWritersTemplate.ResponseClassName, throwOnError: true)!;
        var write = (Delegate)writers.GetField(Plan!.EntryMethod + "Delegate", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
               {
                   Encoder = global::Pragmatic.Serialization.GeneratedJsonDefaults.ResponseEncoder,
                   SkipValidation = true,
               }))
            write.DynamicInvoke(writer, value);

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    ///     What the serializer writes for the value under the host's options: ASP.NET's <c>JsonOptions</c> as they
    ///     start, and what the generated entry point sets on them, with the infrastructure modifier when the host has
    ///     persistence.
    /// </summary>
    /// <remarks>
    ///     ⚠️ ASP.NET's, not <c>JsonSerializerDefaults.Web</c>: they differ in the encoder, and the encoder decides
    ///     how every non-ASCII and HTML-sensitive character is written.
    /// </remarks>
    public static string Serialize(object value, Type declared, bool excludesInfrastructure)
    {
        IJsonTypeInfoResolver resolver = new DefaultJsonTypeInfoResolver();
        if (excludesInfrastructure)
            resolver = resolver.WithAddedModifier(global::Pragmatic.Persistence.Serialization.EntityJsonModifier.ExcludeInfrastructureProperties);

        var options = new Microsoft.AspNetCore.Http.Json.JsonOptions().SerializerOptions;
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.TypeInfoResolver = resolver;
        options.Converters.Add(new JsonStringEnumConverter());

        return JsonSerializer.Serialize(value, declared, options);
    }

    /// <summary>
    ///     Everything the test host loads, which includes the runtime the generated writer calls
    ///     (<c>Utf8JsonValues</c>) and the interfaces that make a type an entity (<c>IChangeTracking</c>).
    /// </summary>
    private static IEnumerable<MetadataReference> References()
        => ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
}
