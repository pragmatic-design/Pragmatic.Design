using System.Collections.Generic;
using System.Collections.Immutable;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;
using Pragmatic.SourceGenerator.Features.Privacy.Models;
using Pragmatic.SourceGenerator.Features.Privacy.Templates;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Privacy;

/// <summary>
///     The operation half of the Article 30 register: which declared operations process personal data,
///     and whether they read it or write it.
/// </summary>
/// <remarks>
///     Driven through the template rather than the whole generator. What is under test is the mapping
///     from endpoint models to <c>ProcessingOperation</c> entries — which endpoints are listed, against
///     which entity, with which access — and the endpoint models are the transform's job, tested where
///     the transform is.
/// </remarks>
public class ProcessingOperationsTemplateTests
{
    private static PrivacyEntityModel Classified(string fullTypeName, string typeName) => new()
    {
        FullTypeName = fullTypeName,
        TypeName = typeName,
        Namespace = "App",
        SubjectIdentifier = "Email",
        Properties =
        [
            new ClassifiedPropertyModel
            {
                Name = "Email",
                TypeDisplay = "string",
                Classification = new PersonalDataModel { Category = "Contact", Erasure = "Null" }
            }
        ]
    };

    /// <summary>An entity holding nothing classified — present in the compilation, absent from the register.</summary>
    private static PrivacyEntityModel Unclassified(string fullTypeName, string typeName) => new()
    {
        FullTypeName = fullTypeName,
        TypeName = typeName,
        Namespace = "App",
        Properties =
        [
            new ClassifiedPropertyModel { Name = "Code", TypeDisplay = "string" }
        ]
    };

    private static EndpointModel Endpoint(
        string typeName,
        string route,
        bool isQuery,
        string? entityType,
        bool isDomainAction = false) => new()
    {
        Namespace = "App",
        TypeName = typeName,
        FullTypeName = "global::App." + typeName,
        Accessibility = "public",
        HttpMethod = isQuery ? "Get" : "Post",
        Route = route,
        IsVoid = false,
        IsDomainAction = isDomainAction,
        IsQuery = isQuery,
        QueryEntityType = isQuery ? entityType : null,
        IsMutation = !isQuery && !isDomainAction,
        MutationEntityType = isQuery ? null : entityType
    };

    private static string Render(
        IReadOnlyList<PrivacyEntityModel> entities,
        IReadOnlyList<EndpointModel> endpoints)
        => new ProcessingActivitySourceTemplate(entities, endpoints, "App.Generated").RenderOutput().Text;

    [Fact]
    public void AQueryOnClassifiedData_IsListedAsARead()
    {
        var text = Render(
            [Classified("App.Member", "Member")],
            [Endpoint("ListMembersQuery", "/members", isQuery: true, "global::App.Member")]);

        text.Should()
            .Contain("\"App.ListMembersQuery\"")
            .And.Contain("ProcessingAccess.Read")
            .And.Contain("\"App.Member\"")
            .And.Contain("\"/members\"",
                "the route is what makes an entry recognisable to somebody who did not write the code");
    }

    [Fact]
    public void AMutationOnClassifiedData_IsListedAsAWrite()
    {
        var text = Render(
            [Classified("App.Member", "Member")],
            [Endpoint("InviteMemberMutation", "/members", isQuery: false, "global::App.Member")]);

        text.Should()
            .Contain("\"App.InviteMemberMutation\"")
            .And.Contain("ProcessingAccess.Write");
    }

    /// <summary>
    ///     The discrimination that makes the list worth reading. Without it every operation in the
    ///     application would be listed, and a register that names everything names nothing.
    /// </summary>
    [Fact]
    public void AnOperationOnUnclassifiedData_IsNotListed()
    {
        var text = Render(
            [Classified("App.Member", "Member"), Unclassified("App.Invoice", "Invoice")],
            [
                Endpoint("ListMembersQuery", "/members", isQuery: true, "global::App.Member"),
                Endpoint("ListInvoicesQuery", "/invoices", isQuery: true, "global::App.Invoice")
            ]);

        text.Should().Contain("\"App.ListMembersQuery\"");
        text.Should().NotContain("ListInvoicesQuery",
            "an entity with no [PersonalData] on it has nothing for Article 30 to record");
    }

    /// <summary>
    ///     A domain action is listed through the entities its dependencies reach.
    /// </summary>
    /// <remarks>
    ///     It names none itself, so the entities come from the transform. An action that only composes —
    ///     no repository of its own, an invoker per mutation — is the shape that would have been missed
    ///     by reading repositories alone, and it is the shape an import takes.
    /// </remarks>
    [Fact]
    public void ADomainAction_IsListedThroughTheEntitiesItsDependenciesReach()
    {
        var text = Render(
            [Classified("App.Member", "Member")],
            [
                Endpoint("ImportMembersAction", "/members/import", isQuery: false, null, isDomainAction: true)
                    with
                    {
                        DomainActionEntityTypes = ImmutableArray.Create("global::App.Member")
                    }
            ]);

        text.Should()
            .Contain("\"App.ImportMembersAction\"")
            .And.Contain("ProcessingAccess.Write",
                "an action is not a query, and overstating is the safe direction here");
    }

    /// <summary>
    ///     An action whose dependencies say nothing stays out, rather than being listed against a guess.
    /// </summary>
    [Fact]
    public void ADomainActionWithNoDerivableEntity_IsNotListed()
    {
        var text = Render(
            [Classified("App.Member", "Member")],
            [Endpoint("RebuildIndexAction", "/index/rebuild", isQuery: false, null, isDomainAction: true)]);

        text.Should().NotContain("RebuildIndexAction");
    }

    /// <summary>
    ///     An action that composes several mutations processes several entities, and all of them appear.
    /// </summary>
    /// <remarks>
    ///     Listing only the first would understate exactly the operation the register most needs to
    ///     describe — the one that touches the most.
    /// </remarks>
    [Fact]
    public void ADomainActionReachingTwoEntities_IsListedAgainstBoth()
    {
        var text = Render(
            [Classified("App.Member", "Member"), Classified("App.Contact", "Contact")],
            [
                Endpoint("MergePeopleAction", "/people/merge", isQuery: false, null, isDomainAction: true)
                    with
                    {
                        DomainActionEntityTypes =
                            ImmutableArray.Create("global::App.Member", "global::App.Contact")
                    }
            ]);

        text.Should().Contain("\"App.Member\"").And.Contain("\"App.Contact\"");
    }

    [Fact]
    public void TheOperationsAreExposedThroughTheInterfaceTheRegisterReads()
    {
        var text = Render(
            [Classified("App.Member", "Member")],
            [Endpoint("ListMembersQuery", "/members", isQuery: true, "global::App.Member")]);

        text.Should().Contain("GetOperationsAsync",
            "the register collects through IProcessingActivitySource; a list nothing can reach is not " +
            "in the register");
    }

    /// <summary>
    ///     The register states what the handler can actually do, not what the attribute asked for.
    /// </summary>
    /// <remarks>
    ///     A query declaring <c>[RecordAccess]</c> in a module without the audit package records nothing.
    ///     Reporting it as recorded would be the single wrong row that discredits the document; PRAG2910
    ///     is what tells the author, and this is what keeps the register honest meanwhile.
    /// </remarks>
    [Fact]
    public void ARecordedRead_IsMarkedRecorded_AndADeclaredOneThatCannotRecordIsNot()
    {
        var recorded = Render(
            [Classified("App.Member", "Member")],
            [
                Endpoint("ExportMembersQuery", "/members/export", isQuery: true, "global::App.Member")
                    with { DeclaresRecordAccess = true, CanRecordAccess = true }
            ]);

        var declaredOnly = Render(
            [Classified("App.Member", "Member")],
            [
                Endpoint("ExportMembersQuery", "/members/export", isQuery: true, "global::App.Member")
                    with { DeclaresRecordAccess = true, CanRecordAccess = false }
            ]);

        // The template writes CRLF on every OS (CSharpTemplate.ToSourceText), so Environment.NewLine
        // matched it only on Windows and this failed on the Linux CI.
        recorded.ReplaceLineEndings("\n").Should().Contain("\"/members/export\",\n            true");
        declaredOnly.ReplaceLineEndings("\n").Should().Contain("\"/members/export\",\n            false");
    }

    /// <summary>
    ///     No purpose is emitted for any operation.
    /// </summary>
    /// <remarks>
    ///     Deliberate, and asserted so it stays deliberate: a purpose is a decision the controller takes,
    ///     and a generator that invented plausible ones would produce a register that reads as
    ///     authoritative and is not.
    /// </remarks>
    [Fact]
    public void NoPurposeIsInvented()
    {
        var text = Render(
            [Classified("App.Member", "Member")],
            [Endpoint("ListMembersQuery", "/members", isQuery: true, "global::App.Member")]);

        text.Should().NotContain("Purpose");
    }
}
