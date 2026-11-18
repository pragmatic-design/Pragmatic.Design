using Basic.Reference.Assemblies;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Jobs.Tests.Generator;

/// <summary>
///     The job invoker reads its parameters through <c>JsonSerializerOptions.GetTypeInfo&lt;T&gt;()</c> where the consumer
///     can see it (System.Text.Json 11), and through <c>GetTypeInfo(typeof(T))</c> and a cast where it cannot (#834).
/// </summary>
/// <remarks>
///     The generator is one assembly for every consumer, so it has to decide per compilation. The typed form throws a
///     clear error when the type has no metadata; the cast form throws an <c>InvalidCastException</c> on the same mistake.
///     Both compilations must build: the net11.0 one proves the emitted call exists where it is emitted.
/// </remarks>
public class TheInvokerReadsTypedMetadataWhereTheConsumerHasItTests : JobsGeneratorTestBase
{
    private const string JobWithParameters = """
        #nullable enable
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Jobs;
        using Pragmatic.Jobs.Attributes;

        namespace TestApp.Processing;

        public record SendEmailParams(string To, string Subject);

        [Job]
        public partial class SendEmailJob : IJob<SendEmailParams>
        {
            public Task ExecuteAsync(SendEmailParams parameters, JobContext context, CancellationToken ct)
                => Task.CompletedTask;
        }
        """;

    private const string TypedRead = "jsonOptions.GetTypeInfo<global::TestApp.Processing.SendEmailParams>()";
    private const string CastRead = "jsonOptions.GetTypeInfo(typeof(global::TestApp.Processing.SendEmailParams))";

    [Fact]
    public void A_consumer_on_net11_reads_the_parameters_through_the_typed_overload()
    {
        var result = GeneratorTestHelper.RunGeneratorOnFramework<PragmaticSourceGenerator>(
            JobWithParameters, Net110.References.All, PackageReferences());

        var invoker = GetGeneratedSource(result, "SendEmailJob.Invoker");
        invoker.Should().Contain(TypedRead);
        invoker.Should().NotContain(CastRead);
        GetCompilationErrors(result).Should().BeEmpty();
    }

    /// <summary>
    ///     The type registry reads and writes the parameters the same way (#838). Its net10.0 output, with the cast, is pinned by
    ///     <c>JobsGeneratorTests.OneOffJob_WithParameters_GeneratesInvokerAndRegistry</c>; this snapshot is the net11.0 one beside it.
    /// </summary>
    [Fact]
    public async Task A_consumer_on_net11_gets_a_type_registry_on_the_typed_overload()
    {
        var result = GeneratorTestHelper.RunGeneratorOnFramework<PragmaticSourceGenerator>(
            JobWithParameters, Net110.References.All, PackageReferences());

        var registry = GetGeneratedSource(result, "_Infra.Jobs.TypeRegistry");
        registry.Should().Contain("JsonSerializer.Deserialize(json, _options.GetTypeInfo<global::TestApp.Processing.SendEmailParams>())");
        registry.Should().Contain("_options.GetTypeInfo<global::TestApp.Processing.SendEmailParams>())");
        registry.Should().NotContain("GetTypeInfo(typeof(global::TestApp.Processing.SendEmailParams))");
        GetCompilationErrors(result).Should().BeEmpty();

        await Verify(registry);
    }

    [Fact]
    public void A_consumer_on_net10_keeps_the_cast_it_has_today()
    {
        var invoker = GetGeneratedSource(RunGenerator(JobWithParameters), "SendEmailJob.Invoker");

        invoker.Should().Contain(CastRead);
        invoker.Should().NotContain(TypedRead);
    }

    /// <summary>
    ///     The assemblies a job's invoker names, minus the ones the net11.0 reference set brings itself
    ///     (System.Text.Json, the Microsoft.Extensions abstractions): a second, net10.0 copy of those would make every
    ///     type they declare ambiguous (CS0433).
    /// </summary>
    private static MetadataReference[] PackageReferences()
    {
        var framework = Net110.References.All
            .Select(static r => Path.GetFileNameWithoutExtension(r.FilePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        MetadataReference[] invokerNames =
        [
            GeneratorTestHelper.FromType<IJob>(),
            GeneratorTestHelper.FromType<Attributes.JobAttribute>(),
            GeneratorTestHelper.FromType<Pragmatic.Resilience.Attributes.RetryAttribute>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Ensure.Ensure)),
            GeneratorTestHelper.FromType<Microsoft.Extensions.DependencyInjection.IServiceCollection>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions)),
            GeneratorTestHelper.FromType<Microsoft.Extensions.Logging.ILogger>(),
            GeneratorTestHelper.FromType<Microsoft.Extensions.Logging.Abstractions.NullLogger>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Composition.Metadata.MetadataCategory)),
            GeneratorTestHelper.FromType<Microsoft.Extensions.Hosting.IHostedService>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(Microsoft.Extensions.Options.IOptions<>)),
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Serialization.PragmaticJsonOptions)),
        ];

        return [.. invokerNames.Where(r => r is PortableExecutableReference pe
            && !framework.Contains(Path.GetFileNameWithoutExtension(pe.FilePath)))];
    }
}
