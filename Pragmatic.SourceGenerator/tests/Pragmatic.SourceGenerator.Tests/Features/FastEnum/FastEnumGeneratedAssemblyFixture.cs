using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.SourceGenerator.Tests.Features.FastEnum;

/// <summary>
///     Runs the real generator over a small set of [FastEnum] enums, emits the resulting
///     compilation to an in-memory assembly and loads it, so tests can execute the generated
///     converters instead of pattern-matching their source text.
///     <para>
///         Two <see cref="JsonSerializerOptions"/> are exposed, differing only in the enum
///         converter: <see cref="SystemOptions"/> uses <see cref="JsonStringEnumConverter"/>
///         (what the generated host entry point registers today) and <see cref="GeneratedOptions"/>
///         uses the generated per-enum converters. Everything else mirrors
///         <c>PragmaticEntryTemplate.RenderJsonDefaults</c>.
///     </para>
/// </summary>
public sealed class FastEnumGeneratedAssemblyFixture
{
    private const string Source = """
        namespace Pragmatic
        {
            [System.AttributeUsage(System.AttributeTargets.Enum)]
            public sealed class FastEnumAttribute : System.Attribute { }
        }

        namespace Sample
        {
            [Pragmatic.FastEnum]
            public enum OrderStatus { Pending, Confirmed, Cancelled }

            [Pragmatic.FastEnum]
            [System.Flags]
            public enum Perm { None = 0, Read = 1, Write = 2 }

            [Pragmatic.FastEnum]
            [System.Flags]
            public enum Bits { A = 1, B = 2 }

            [Pragmatic.FastEnum]
            public enum Level : byte { Low = 0, High = 1 }

            public sealed class Wrapper { public OrderStatus Status { get; set; } }
        }
        """;

    public FastEnumGeneratedAssemblyFixture()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            Source,
            GeneratorTestHelper.FromTypeAssembly(typeof(JsonConverter<int>)));

        GeneratorTestHelper.HasCompilationErrors(result).Should().BeFalse(
            "the generated FastEnum code must compile: " + string.Join("; ", result.OutputCompilation
                .GetDiagnostics()
                .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                .Select(d => d.ToString())));

        using var stream = new MemoryStream();
        var emit = result.OutputCompilation.Emit(stream);
        emit.Success.Should().BeTrue(string.Join("; ", emit.Diagnostics
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(d => d.ToString())));

        var assembly = Assembly.Load(stream.ToArray());

        OrderStatus = Resolve(assembly, "Sample.OrderStatus");
        Perm = Resolve(assembly, "Sample.Perm");
        Level = Resolve(assembly, "Sample.Level");
        Bits = Resolve(assembly, "Sample.Bits");
        Wrapper = Resolve(assembly, "Sample.Wrapper");

        SystemOptions = BuildOptions(new JsonStringEnumConverter());
        GeneratedOptions = BuildOptions(
            NewConverter(assembly, "Sample.OrderStatusJsonConverter"),
            NewConverter(assembly, "Sample.PermJsonConverter"),
            NewConverter(assembly, "Sample.LevelJsonConverter"),
            NewConverter(assembly, "Sample.BitsJsonConverter"));
    }

    /// <summary>The generated, non-flags enum with an <c>int</c> underlying type.</summary>
    public Type OrderStatus { get; }

    /// <summary>The generated <c>[Flags]</c> enum.</summary>
    public Type Perm { get; }

    /// <summary>The generated enum with a <c>byte</c> underlying type.</summary>
    public Type Level { get; }

    /// <summary>A <c>[Flags]</c> enum with NO zero-valued member — the "no flags set" state has no name.</summary>
    public Type Bits { get; }

    /// <summary>A class with an enum property — proves the naming policy does not touch enum values.</summary>
    public Type Wrapper { get; }

    /// <summary>Host defaults with <see cref="JsonStringEnumConverter"/> — the behaviour to match.</summary>
    public JsonSerializerOptions SystemOptions { get; }

    /// <summary>Host defaults with the generated per-enum converters.</summary>
    public JsonSerializerOptions GeneratedOptions { get; }

    private static Type Resolve(Assembly assembly, string fullName)
        => assembly.GetType(fullName)
           ?? throw new InvalidOperationException($"The generated assembly does not contain '{fullName}'.");

    private static JsonConverter NewConverter(Assembly assembly, string fullName)
        => (JsonConverter)Activator.CreateInstance(Resolve(assembly, fullName))!;

    private static JsonSerializerOptions BuildOptions(params JsonConverter[] converters)
    {
        // Mirrors PragmaticEntryTemplate.RenderJsonDefaults: the options the host actually uses.
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReferenceHandler = ReferenceHandler.IgnoreCycles
        };

        foreach (var converter in converters)
            options.Converters.Add(converter);

        return options;
    }
}
