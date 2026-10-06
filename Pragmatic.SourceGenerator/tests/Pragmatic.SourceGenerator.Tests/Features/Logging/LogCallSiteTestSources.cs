using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Pragmatic.SourceGen.Testing;

namespace Pragmatic.SourceGenerator.Tests.Features.Logging;

/// <summary>What a call-site test compiles against: the real attribute, logger and JSON writer.</summary>
/// <remarks>
///     Real assemblies rather than stubs: the generated code calls <c>ILogger.Log&lt;TState&gt;</c>,
///     <c>Utf8JsonWriter</c> and the runtime helpers, and a stub of any of them would let a body compile
///     here that does not compile in a project.
/// </remarks>
internal static class LogCallSiteTestSources
{
    /// <summary>The alias the generator's package declares through MSBuild.</summary>
    public const string Alias = "global using LoggerMessageAttribute = global::Pragmatic.Logging.CallSites.LoggerMessageAttribute;\n";

    public static MetadataReference[] References =>
    [
        GeneratorTestHelper.FromType<ILogger>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Logging.CallSites.LoggerMessageAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Privacy.PersonalDataAttribute>(),
        GeneratorTestHelper.FromType<System.Text.Json.Utf8JsonWriter>(),
        GeneratorTestHelper.FromTypeAssembly(typeof(System.Text.Encodings.Web.JavaScriptEncoder)),
        GeneratorTestHelper.FromTypeAssembly(typeof(System.Buffers.ArrayPool<>)),
        GeneratorTestHelper.FromTypeAssembly(typeof(System.Text.Encoding)),
    ];

    public static SourceGenRunResult Run(string source)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Alias + source, References);
}
