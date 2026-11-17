using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.SourceGen;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Core;

/// <summary>
///     What the generator reads about the consumer's compilation (#834): computed once, value-equatable, and read from
///     the compilation itself. The positive capability — a consumer on net11.0 getting the typed form — is proved end to
///     end in Pragmatic.Jobs.Tests (TheInvokerReadsTypedMetadataWhereTheConsumerHasItTests), against the .NET 11
///     reference assemblies.
/// </summary>
public class ConsumerPlatformTests
{
    // A job, so an output reads the platform: an incremental generator evaluates only the nodes an output depends on.
    private const string JobSource = """
        namespace Pragmatic.Jobs.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class JobAttribute : System.Attribute { }
        }
        namespace MyApp
        {
            [Pragmatic.Jobs.Attributes.Job]
            public partial class SendWelcomeEmailJob { }
        }
        """;

    [Fact]
    public void Adding_an_unrelated_class_leaves_the_platform_cached()
    {
        var result = GeneratorTestHelper.RunGeneratorIncremental<PragmaticSourceGenerator>(
            JobSource, IncrementalityAssert.UnrelatedAddition);

        IncrementalityAssert.StepsStayCached(result, TrackingNames.CoreConsumerPlatform, TrackingNames.JobsOneOffJobs);
    }

    [Fact]
    public void The_language_version_is_the_compilation_s()
    {
        var csharp12 = Compile(new CSharpParseOptions(LanguageVersion.CSharp12));
        var csharp13 = Compile(new CSharpParseOptions(LanguageVersion.CSharp13));

        ConsumerPlatformDetector.Detect(csharp12).LanguageVersion.Should().Be(1200);
        ConsumerPlatformDetector.Detect(csharp13).LanguageVersion.Should().Be(1300);
    }

    [Fact]
    public void System_Text_Json_10_has_no_typed_GetTypeInfo()
    {
        var withJson10 = Compile(new CSharpParseOptions(LanguageVersion.CSharp13),
            MetadataReference.CreateFromFile(typeof(System.Text.Json.JsonSerializerOptions).Assembly.Location));

        typeof(System.Text.Json.JsonSerializerOptions).Assembly.GetName().Version!.Major.Should().Be(10,
            "the negative case is about the System.Text.Json the tests run on");
        ConsumerPlatformDetector.Detect(withJson10).HasJsonTypeInfoOfT.Should().BeFalse();
    }

    [Fact]
    public void A_compilation_without_System_Text_Json_has_the_baseline()
    {
        var platform = ConsumerPlatformDetector.Detect(Compile(new CSharpParseOptions(LanguageVersion.CSharp13)));

        platform.HasJsonTypeInfoOfT.Should().BeFalse();
        (platform with { LanguageVersion = 0 }).Should().Be(ConsumerPlatform.Baseline);
    }

    private static CSharpCompilation Compile(CSharpParseOptions options, params MetadataReference[] references)
        => CSharpCompilation.Create(
            "PlatformProbe",
            [CSharpSyntaxTree.ParseText("public class C { }", options)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location), .. references],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
}
