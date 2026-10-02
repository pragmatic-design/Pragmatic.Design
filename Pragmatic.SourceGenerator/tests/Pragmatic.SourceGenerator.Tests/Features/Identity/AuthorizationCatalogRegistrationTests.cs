using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen.Testing;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Identity;

/// <summary>
///     The Identity feature must emit a DI registration that feeds the generated
///     <c>PermissionRegistry.All</c>/<c>RoleRegistry.All</c> into <c>DefaultPermissionCatalog</c>,
///     plus the Authorization metadata attribute the host uses to invoke it. Without it the
///     registries are generated but never registered, and <c>IPermissionCatalog</c> is always empty.
///     Marker types are stubbed in source so FeatureDetector/CompositionDetector trigger without the
///     runtime packages; assertions are on generated text, not compilation success.
/// </summary>
public class AuthorizationCatalogRegistrationTests
{
    private const string AuthorizationStubs = """
        namespace Pragmatic.Authorization
        {
            [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = true)]
            public sealed class PermissionAttribute : System.Attribute
            {
                public PermissionAttribute(string value, string description) { }
                public string? Category { get; set; }
            }
            public interface IRole { }
            public sealed record PermissionInfo(string Name, string? Description, string? Category);
            public sealed record RoleInfo(string Name, string? Description, System.Collections.Generic.IReadOnlyList<string> DefaultPermissions);
            // Marker type gating FeatureDetector.HasAuthorization.
            public static class PragmaticBuilderAuthorizationExtensions { }
        }
        """;

    private const string CompositionStubs = """
        namespace Pragmatic.Composition.Hosting { public class PragmaticBuilder { } }
        """;

    // An assembly attribute precedes every namespace, so this goes first in each source.
    private const string Declared = """
        [assembly: Pragmatic.Authorization.Permission("billing.invoice.refund", "Refund a paid invoice", Category = "Billing")]

        """;

    private const string PermissionsAndRoles = """
        namespace Sample.Billing
        {
            using Pragmatic.Authorization;
            using System.Collections.Generic;

            public sealed class BillingClerkRole : IRole
            {
                public static string Name => "billing-clerk";
                public static string? Description => "Handles billing";
                public static IReadOnlyList<string> DefaultPermissions => new[] { "billing.invoice.refund" };
            }
        }
        """;

    [Fact]
    public void PermissionsAndRoles_GenerateAuthorizationCatalogRegistration()
    {
        var source = Declared + AuthorizationStubs + PermissionsAndRoles;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);
        var registration = GeneratorTestHelper.GetGeneratedSource(result, "Identity.AuthorizationRegistration");

        registration.Should().NotBeNull(
            "the SG must emit a DI registration so PermissionRegistry.All/RoleRegistry.All are consumed");
        registration!.Should()
            .Contain("AddGeneratedAuthorizationCatalog")
            .And.Contain("PermissionRegistry.All")
            .And.Contain("RoleRegistry.All")
            .And.Contain("AddSingleton");
    }

    [Fact]
    public void WithComposition_EmitsAuthorizationMetadataForHostAggregation()
    {
        var source = Declared + AuthorizationStubs + CompositionStubs + PermissionsAndRoles;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);
        var metadata = GeneratorTestHelper.GetGeneratedSource(result, "_Metadata.Authorization");

        metadata.Should().NotBeNull(
            "when Composition is referenced the host must be able to discover the catalog registration");
        metadata!.Should()
            .Contain("PragmaticMetadataAttribute")
            .And.Contain("MetadataCategory)22")
            .And.Contain("AuthorizationRegistrationExtensions.AddGeneratedAuthorizationCatalog");
    }

    [Fact]
    public void WithoutComposition_DoesNotEmitAuthorizationMetadata()
    {
        var source = Declared + AuthorizationStubs + PermissionsAndRoles;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);
        var metadata = GeneratorTestHelper.GetGeneratedSource(result, "_Metadata.Authorization");

        metadata.Should().BeNull("metadata is only meaningful for host aggregation (Composition referenced)");
    }
}
