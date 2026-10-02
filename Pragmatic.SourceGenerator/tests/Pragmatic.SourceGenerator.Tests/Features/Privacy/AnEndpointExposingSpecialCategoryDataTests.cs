using System.Linq;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Privacy;

/// <summary>
///     PRAG2904 from source: a query endpoint over an entity with special-category data, reported when it names
///     nobody who may read it and silent when it requires a permission.
/// </summary>
/// <remarks>
///     <para>
///         <c>SpecialCategoryExposureTests</c> decides the rule on hand-built models. What it cannot see is the
///         wiring: the endpoint pipeline delivering its models to the privacy feature, the two naming the entity
///         the same way, the finding becoming a diagnostic.
///     </para>
///     <para>
///         ⚠️ The endpoint names the entity fully qualified (<c>global::App.Patient</c>) and the privacy model
///         without the prefix (<c>App.Patient</c>); the hand-built models used one spelling for both.
///     </para>
/// </remarks>
public class AnEndpointExposingSpecialCategoryDataTests
{
    /// <summary>The query attribute and the permission attribute; the real ones ship in their packages.</summary>
    private const string EndpointStubs = """
        namespace Pragmatic.Persistence.Query.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class QueryAttribute<TEntity, TResult> : System.Attribute { }
        }
        namespace Pragmatic.Authorization
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class RequirePermissionAttribute : System.Attribute
            {
                public RequirePermissionAttribute(params string[] permissions) { }
            }
        }
        """;

    private static string Source(string authorization) => PrivacyTestSources.Stubs + PrivacyTestSources.OperationStubs
        + EndpointStubs + $$"""

        namespace App
        {
            using Pragmatic.Privacy;

            [DataSubject("Email")]
            public class Patient
            {
                [PersonalData(DataCategory.Contact, Erasure = ErasureStrategy.Null)]
                public string? Email { get; set; }

                [PersonalData(DataCategory.Special, Erasure = ErasureStrategy.Null)]
                public string? Diagnosis { get; set; }
            }

            [Pragmatic.Persistence.Query.Attributes.Query<Patient, Patient>]
            [Pragmatic.Endpoints.Attributes.Endpoint(Pragmatic.Endpoints.HttpVerb.Get, "api/patients")]
            {{authorization}}
            public partial class ListPatientsQuery { }
        }
        """;

    private static Microsoft.CodeAnalysis.Diagnostic[] Prag2904(string authorization)
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Source(authorization), []);
        return GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG2904").ToArray();
    }

    [Fact]
    public void AQueryEndpointThatNamesNobody_IsReported_OnTheEndpoint()
    {
        var reported = Prag2904(authorization: "");

        reported.Should().ContainSingle("the endpoint exposes the one special-category property");
        reported[0].GetMessage().Should().Contain("Diagnosis").And.Contain("ListPatientsQuery");
        var endpointLine = Source("").Split('\n').ToList().FindIndex(l => l.Contains("class ListPatientsQuery"));
        reported[0].Location.GetLineSpan().StartLinePosition.Line.Should().Be(endpointLine,
            "it is reported where the fix goes: on the endpoint, not on the property");
    }

    /// <summary>
    ///     The control: the same endpoint requiring a permission. Without it, "reported" is satisfied by a feature
    ///     that reports every endpoint over the entity, whatever it declares.
    /// </summary>
    [Fact]
    public void TheSameEndpointRequiringAPermission_IsNotReported()
    {
        Prag2904(authorization: "[Pragmatic.Authorization.RequirePermission(\"clinic.patient.read\")]")
            .Should().BeEmpty();
    }
}
