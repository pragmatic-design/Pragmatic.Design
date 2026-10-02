using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;
using Pragmatic.SourceGenerator.Features.Privacy.Models;
using Pragmatic.SourceGenerator.Features.Privacy.Transforms;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Privacy;

/// <summary>
///     The rule behind PRAG2904: which entity/endpoint pairs count as over-exposure.
/// </summary>
/// <remarks>
///     Tested against constructed models rather than through the whole generator. Driving the endpoint
///     pipeline from stubbed source would mean a large stub whose fidelity to the real transform nothing
///     checks — and the interesting part here is the decision, not the plumbing that delivers it.
///     The wiring — endpoint pipeline, privacy feature, diagnostic — is <c>AnEndpointExposingSpecialCategoryDataTests</c>,
///     from source. ⚠️ The models here spell the entity one way for both sides; the real ones need not, and the rule
///     can be right here while the diagnostic never fires — which is why the source-level test exists.
/// </remarks>
public class SpecialCategoryExposureTests
{
    private const string EntityName = "Patient";
    private const string EntityFullName = "Clinic.Patient";

    private static PrivacyEntityModel Entity(string category = "Special", string? fullName = null)
        => new()
        {
            FullTypeName = fullName ?? EntityFullName,
            TypeName = EntityName,
            Namespace = "Clinic",
            Properties = new EquatableArray<ClassifiedPropertyModel>(
            [
                new ClassifiedPropertyModel
                {
                    Name = "Diagnosis",
                    TypeDisplay = "string",
                    Classification = new PersonalDataModel { Category = category, Erasure = "Null" },
                },
            ]),
        };

    private static EndpointModel Endpoint(AuthorizationModel? authorization, string? queryEntity = EntityFullName)
        => new()
        {
            Namespace = "Clinic.Api",
            TypeName = "GetPatient",
            FullTypeName = "Clinic.Api.GetPatient",
            Accessibility = "public",
            HttpMethod = "Get",
            Route = "/patients/{id}",
            IsVoid = false,
            IsDomainAction = false,
            QueryEntityType = queryEntity,
            Authorization = authorization,
        };

    private static List<SpecialCategoryExposure.Finding> Find(PrivacyEntityModel entity, EndpointModel endpoint)
        => SpecialCategoryExposure.Find([entity], ImmutableArray.Create(endpoint));

    [Fact]
    public void SpecialCategoryBehindAnEndpointWithNoAuthorization_IsReported()
    {
        var findings = Find(Entity(), Endpoint(authorization: null));

        findings.Should().ContainSingle();
        findings[0].Property.Name.Should().Be("Diagnosis");
        findings[0].Endpoint.TypeName.Should().Be("GetPatient");
    }

    [Fact]
    public void AnonymousAccess_IsReportedEvenWhenAPolicyIsNamed()
    {
        // AllowAnonymous wins at runtime, so a policy sitting next to it protects nothing.
        var authorization = new AuthorizationModel
        {
            AllowAnonymous = true,
            PolicyName = "CanReadPatients",
        };

        Find(Entity(), Endpoint(authorization)).Should().ContainSingle();
    }

    [Fact]
    public void AuthenticationWithoutAPolicy_IsStillReported()
    {
        // The case this diagnostic exists for. "Requires a signed-in user" means every account in the
        // system can read the data, which for a special category is the finding worth making — and it
        // is the configuration most likely to be mistaken for protection.
        Find(Entity(), Endpoint(new AuthorizationModel { IsRequired = true })).Should().ContainSingle();
    }

    [Theory]
    [InlineData("policy")]
    [InlineData("required-permission")]
    [InlineData("any-permission")]
    [InlineData("role")]
    public void AnEndpointThatNamesWhoMayReadIt_IsNotReported(string constraint)
    {
        var authorization = constraint switch
        {
            "policy" => new AuthorizationModel { IsRequired = true, PolicyName = "CanReadPatients" },
            "required-permission" => new AuthorizationModel
            {
                IsRequired = true,
                RequiredPermissions = new EquatableArray<string>(["patients.read"]),
            },
            "any-permission" => new AuthorizationModel
            {
                IsRequired = true,
                AnyPermissions = new EquatableArray<string>(["patients.read", "clinic.admin"]),
            },
            _ => new AuthorizationModel
            {
                IsRequired = true,
                RequiredRoles = new EquatableArray<string>(["Clinician"]),
            },
        };

        Find(Entity(), Endpoint(authorization)).Should().BeEmpty();
    }

    [Fact]
    public void OrdinaryPersonalData_IsNotReported()
    {
        // Every category other than Special is out of scope. Reporting them would make this warning
        // fire on nearly every endpoint in a real application, which is how a warning gets suppressed.
        Find(Entity(category: "Contact"), Endpoint(authorization: null)).Should().BeEmpty();
    }

    [Fact]
    public void AnEndpointOverADifferentEntity_IsNotReported()
    {
        Find(Entity(), Endpoint(authorization: null, queryEntity: "Clinic.Appointment")).Should().BeEmpty();
    }

    [Fact]
    public void AnEndpointReturningTheEntityDirectly_IsReported()
    {
        // Most endpoints return a DTO and will not match. One that hands back the entity is exactly the
        // case that must not slip through on the grounds that it declared no query entity.
        var endpoint = Endpoint(authorization: null, queryEntity: null) with { ResponseType = EntityFullName };

        Find(Entity(), endpoint).Should().ContainSingle();
    }

    [Fact]
    public void NoEndpoints_ReportsNothing()
    {
        SpecialCategoryExposure.Find([Entity()], ImmutableArray<EndpointModel>.Empty).Should().BeEmpty();
        SpecialCategoryExposure.Find([Entity()], default).Should().BeEmpty();
    }

    [Fact]
    public void TwoEndpointsOverTheSameEntity_AreBothReported()
    {
        // One finding per endpoint, not one per property: the fix is per endpoint, and collapsing them
        // would leave the second one silently unaddressed.
        var findings = SpecialCategoryExposure.Find(
            [Entity()],
            ImmutableArray.Create(
                Endpoint(authorization: null),
                Endpoint(authorization: null) with { TypeName = "ListPatients", Route = "/patients" }));

        findings.Should().HaveCount(2);
        findings.Select(f => f.Endpoint.TypeName).Should().BeEquivalentTo(["GetPatient", "ListPatients"]);
    }
}
