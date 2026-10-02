using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Privacy;

/// <summary>
///     The last joint: the host has to <em>call</em> the generated privacy registration. Generating a
///     registration nobody invokes repeats the defect this layer exists to fix, one level up.
/// </summary>
/// <remarks>
///     Nothing here fails loudly when the call is missing. <c>SubjectAccessService</c> and
///     <c>ErasureOrchestrator</c> both take an <c>IEnumerable&lt;&gt;</c>, and an empty one produces an
///     export with no data and an erasure with no changes, each reporting success.
/// </remarks>
public class PrivacyHostWiringTests
{
    private const string HostStubs = """
        namespace Pragmatic.Composition.Hosting
        {
            public class PragmaticBuilder { }
        }
        public static class Program
        {
            public static void Main() { }
        }
        """;

    private const string ClassifiedEntity = """

        namespace App
        {
            using Pragmatic.Privacy;

            [DataSubject("Email")]
            public class Customer
            {
                [PersonalData(DataCategory.Contact, Erasure = ErasureStrategy.Null)]
                public string Email { get; set; } = "";
            }
        }
        """;

    private static string? HostServices(SourceGenRunResult result)
        => GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Where(kv => kv.Key.Contains("Host.Services"))
            .Select(kv => kv.Value)
            .FirstOrDefault();

    [Fact]
    public void HostDeclaringAClassifiedEntity_CallsTheGeneratedPrivacyRegistration()
    {
        var result = GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(
            PrivacyTestSources.Stubs + PrivacyTestSources.AdapterStubs + HostStubs + ClassifiedEntity, []);

        HostServices(result).Should().NotBeNull();
        HostServices(result)!.Should().Contain(
            "global::TestAssembly.Generated.PragmaticPrivacyRegistration.AddGeneratedPrivacyAdapters(services);",
            "the adapters are contributed to DI, and nothing discovers them");
    }

    [Fact]
    public void HostWithNoClassifiedData_DoesNotCallAnything()
    {
        var result = GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(
            PrivacyTestSources.Stubs + PrivacyTestSources.AdapterStubs + HostStubs, []);

        (HostServices(result) ?? string.Empty).Should().NotContain("PragmaticPrivacyRegistration");
    }
}
