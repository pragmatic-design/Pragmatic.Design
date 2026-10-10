using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Privacy;

/// <summary>
///     PRAG2910: an operation asks for its reads to be recorded, and there is nothing to record into.
/// </summary>
/// <remarks>
///     The attribute is used exactly where the answer to "who looked at this" has to exist later, and
///     that evidence is either written at the time or never. Emitting nothing in silence would leave the
///     author believing the record is being kept, and the gap would surface as the one question it was
///     put there to answer.
/// </remarks>
public class RecordAccessDiagnosticTests
{
    private const string Subject = """

        namespace App
        {
            using Pragmatic.Privacy;

            [DataSubject("Email")]
            public class Patient
            {
                [PersonalData(DataCategory.Special, Erasure = ErasureStrategy.Null)]
                public string Email { get; set; } = "";
            }

            [Pragmatic.Actions.Attributes.DomainAction]
            [Pragmatic.Endpoints.Attributes.Endpoint(Pragmatic.Endpoints.HttpVerb.Get, "api/patients/export")]
            [RecordAccess]
            public partial class ExportPatientsAction : Pragmatic.Actions.Abstractions.VoidDomainAction
            {
                private Pragmatic.Persistence.Repository.IRepository<Patient> _patients = null!;
            }
        }
        """;

    private static SourceGenRunResult Run()
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            PrivacyTestSources.Stubs + PrivacyTestSources.AdapterStubs
            + PrivacyTestSources.OperationStubs + Subject,
            []);

    [Fact]
    public void RecordAccessWithoutTheAuditPackage_IsReported()
    {
        // The stubs carry no Pragmatic.Audit.IAuditTrail, which is the situation being reported: the
        // attribute compiles, and nothing it promises can happen.
        GeneratorTestHelper.HasDiagnostic(Run(), "PRAG2910").Should().BeTrue();
    }

    [Fact]
    public void TheDiagnosticNamesTheOperation()
    {
        // "Something declares it" is not actionable in an assembly with a hundred operations.
        var diagnostic = GeneratorTestHelper.GetDiagnosticsById(Run(), "PRAG2910").Single();

        diagnostic.GetMessage().Should().Contain("ExportPatientsAction");
    }

    [Fact]
    public void TheRegisterAgreesWithTheDiagnostic()
    {
        // The two must not contradict each other: one says the recording cannot happen, so the other
        // must not list the operation as recorded.
        var activities = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(Run())
            .Where(kv => kv.Key.Contains("_Infra.Privacy.ProcessingActivities"))
            .Select(kv => kv.Value)
            .FirstOrDefault();

        activities.Should().NotBeNull();
        activities!.Should().Contain("\"App.ExportPatientsAction\"");

        // The recorded flag is the token after the route, whatever the indentation. The leading slash is
        // optional because the route is normalised on the way into the model, not as declared.
        System.Text.RegularExpressions.Regex
            .IsMatch(activities!, "\"/?api/patients/export\",\\s*false")
            .Should().BeTrue("the attribute could not be honoured, so the register must not claim it was");
    }

    [Fact]
    public void WithoutTheAttribute_NothingIsReported()
    {
        // Guards the suite: a diagnostic that fires on everything would make the assertions above pass
        // without the attribute having anything to do with it.
        // The attribute alone is removed, not its line: the raw string carries the line endings of the
        // checkout, CRLF on Windows, so a pattern ending in "\n" removed nothing there and the attribute
        // stayed.
        var subject = Subject.Replace("[RecordAccess]", string.Empty);
        subject.Should().NotContain("RecordAccess");

        var withoutAttribute = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            PrivacyTestSources.Stubs + PrivacyTestSources.AdapterStubs
            + PrivacyTestSources.OperationStubs + subject,
            []);

        GeneratorTestHelper.HasDiagnostic(withoutAttribute, "PRAG2910").Should().BeFalse();
    }
}
