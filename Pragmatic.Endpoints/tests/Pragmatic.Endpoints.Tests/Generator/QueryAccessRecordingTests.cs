using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     A query records its reads only when it asks to.
/// </summary>
/// <remarks>
///     <para>
///         Writes reach the audit trail through an interceptor over <c>SaveChanges</c>, which never sees
///         a query, so reads reached it nowhere. Recording all of them is the wrong answer — reads
///         outnumber writes by orders of magnitude and the trail would stop being searchable — so it is
///         one operation at a time, through <c>[RecordAccess]</c>.
///     </para>
///     <para>
///         The default is asserted as hard as the opt-in. A recording that switched itself on would put
///         personal data through the trail nobody asked to have it in.
///     </para>
/// </remarks>
public class QueryAccessRecordingTests : EndpointsGeneratorTestBase
{
    private static string Source(string attribute) => $$"""
        using System;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Query.Attributes;

        namespace TestApp;

        public partial class Patient : IEntity
        {
            public Guid PersistenceId { get; set; }
            public string Name { get; set; } = "";
        }

        public partial class PatientDto
        {
            public Guid Id { get; init; }
            public string Name { get; init; } = "";
        }

        [Query<Patient, PatientDto>]
        [Endpoint(HttpVerb.Get, "api/patients")]
        {{attribute}}
        public partial class SearchPatients
        {
            public int Page { get; init; } = 1;
            public int PageSize { get; init; } = 20;
        }
        """;

    private static string? Handler(string attribute)
        => GetGeneratedSource(RunGeneratorWithAudit(Source(attribute)), "SearchPatients.Endpoint");

    [Fact]
    public void WithoutTheAttribute_NothingIsWrittenToTheTrail()
    {
        (Handler(string.Empty) ?? string.Empty).Should().NotContain("IAuditTrail",
            "recording every read is what makes a trail unusable for the reads that matter");
    }

    [Fact]
    public void WithTheAttribute_TheReadIsRecorded()
    {
        var handler = Handler("[Pragmatic.Privacy.RecordAccess]");

        handler.Should().NotBeNull();
        handler!.Should()
            .Contain("global::Pragmatic.Audit.IAuditTrail")
            .And.Contain("\"Privacy.PersonalDataRead\"")
            .And.Contain("\"TestApp.SearchPatients\"",
                "the entry names the operation the Article 30 register keys on");
    }

    [Fact]
    public void TheRecordIsWrittenOnlyAfterTheQuerySucceeded()
    {
        // A read that failed is not a read. Recording before execution would put entries in the trail
        // for rows nobody ever saw.
        var handler = Handler("[Pragmatic.Privacy.RecordAccess]")!;

        // The read is the invoker's; what has to precede the record is the call to it.
        var executeIndex = handler.IndexOf("__invoker.RunAsync", StringComparison.Ordinal);
        var recordIndex = handler.IndexOf("RecordAsync", StringComparison.Ordinal);

        executeIndex.Should().BeGreaterThan(-1);
        recordIndex.Should().BeGreaterThan(executeIndex);
    }

    [Fact]
    public void TheEntryCarriesNoRowsAtAll()
    {
        // The trail must not become a second copy of the personal data it accounts for. Actor,
        // operation and time; never the result.
        var handler = Handler("[Pragmatic.Privacy.RecordAccess]")!;

        handler.Should().NotContain("Detail = ", "a free-form field is how personal data gets back in");
        handler.Should().Contain("ActorRef =");
    }

    [Fact]
    public void AMissingTrailRegistrationDoesNotFailTheRead()
    {
        // An application may reference the audit package without registering a store. Throwing there
        // would turn a missing registration into a failed request for the caller.
        Handler("[Pragmatic.Privacy.RecordAccess]")!.Should().Contain("__trail is not null");
    }
}
