using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Testing.SourceGenerator.Tests;

/// <summary>
///     The generated suite says what it wrote; the coverage report says what it did not, and
///     why.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>An absence has no name.</b> Reading the generated classes tells a reader that eight
///         operations are covered; nothing in them can tell him the application publishes eleven, or that
///         the three missing ones were declined for a reason the generator had and kept to itself. The
///         issue that asked for this was written from a hand comparison against a route table, and two of
///         its three conclusions about <em>why</em> were wrong — because a hand comparison sees the gap
///         and never the reason.
///     </para>
///     <para>
///         Emitted as data, not as a comment: an application pins the list, and a new uncovered operation
///         fails that test instead of joining the silence.
///     </para>
/// </remarks>
public class TheCoverageReportSaysWhatWasNotEmittedTests
{
    private const string Preamble = """
        namespace Pragmatic.Endpoints.Attributes
        {
            public enum HttpVerb { Get, Post, Put, Patch, Delete }
            public sealed class EndpointAttribute : System.Attribute
            {
                public EndpointAttribute(HttpVerb method, string route) { }
            }
        }
        namespace Pragmatic.Authorization
        {
            public sealed class RequirePermissionAttribute : System.Attribute
            {
                public RequirePermissionAttribute(string permission) { }
            }
        }
        namespace Pragmatic.Endpoints.Binding
        {
            public sealed class FromFormAttribute : System.Attribute { }
        }
        namespace Pragmatic.Actions.Mutation { public abstract class Mutation<TEntity> { } }
        namespace App.Billing.Entities { public class Invoice { } }
        """;

    /// <summary>An operation the generator covered says so, and says nothing it did not do.</summary>
    [Fact]
    public void AnOperationWithAPermission_IsReportedAsCovered()
    {
        var report = Generate("""
            namespace App.Billing.Reads
            {
                [Pragmatic.Endpoints.Attributes.Endpoint(Pragmatic.Endpoints.Attributes.HttpVerb.Get, "/api/invoices/{id}")]
                [Pragmatic.Authorization.RequirePermission("billing.invoice.read")]
                public class GetInvoiceEndpoint { }
            }
            """);

        report.Should().Contain("\"GetInvoice\"");
        report.Should().Contain("[\"auth\", \"not-found\"]",
            "a GET by id with a permission gets the refusal pair and the unknown-id contract");
    }

    /// <summary>
    ///     ⚠️ The row this whole file exists for: an operation with no permission is covered by
    ///     <b>nothing</b>, and the report says why instead of leaving it out.
    /// </summary>
    /// <remarks>
    ///     This is what three of Casework's operations were, and what the issue read as "the generator
    ///     does not emit contracts for mutations and queries". It does — measured on Invoicing, where
    ///     <c>UpdateDraftInvoiceMutation</c> has both halves of its pair. What those three had in common
    ///     was no <c>[RequirePermission]</c> at all, and an authorization contract on an operation nothing
    ///     refuses would assert a falsehood.
    /// </remarks>
    [Fact]
    public void AnOperationWithNoPermission_IsReportedAsUncovered_WithTheReason()
    {
        var report = Generate("""
            namespace App.Billing.Writes
            {
                [Pragmatic.Endpoints.Attributes.Endpoint(Pragmatic.Endpoints.Attributes.HttpVerb.Put, "/api/invoices/{id}")]
                public class UpdateInvoiceEndpoint { }
            }
            """);

        report.Should().Contain("\"UpdateInvoice\"");
        report.Should().Contain("[]", "nothing was emitted for it");
        report.Should().Contain("it declares no permission",
            "and the report says why, which is the only place that fact exists");
    }

    /// <summary>
    ///     A create the generator declined says which contract it declined and on what ground.
    /// </summary>
    /// <remarks>
    ///     The multipart upload is the honest case: a JSON body against it answers 415, so the create and
    ///     the isolation contract would fail on the request rather than on the application. Its auth
    ///     contract is emitted all the same, and the row shows both halves at once.
    /// </remarks>
    [Fact]
    public void AMultipartCreate_KeepsItsAuthContract_AndSaysWhyItHasNoCreate()
    {
        var report = Generate("""
            namespace App.Billing.Uploads
            {
                [Pragmatic.Endpoints.Attributes.Endpoint(Pragmatic.Endpoints.Attributes.HttpVerb.Post, "/api/invoices/attachments")]
                [Pragmatic.Authorization.RequirePermission("billing.invoice.update")]
                public class UploadAnAttachmentEndpoint : Pragmatic.Actions.Mutation.Mutation<App.Billing.Entities.Invoice>
                {
                    [Pragmatic.Endpoints.Binding.FromForm]
                    public string File { get; set; }
                }
            }
            """);

        report.Should().Contain("\"UploadAnAttachment\"");
        report.Should().Contain("\"auth\"", "an upload is still refused to a caller without the permission");
        report.Should().Contain("multipart", "and the create's absence names the reason");
    }

    /// <summary>
    ///     The control: an application whose every operation is covered gets a report with no reasons in
    ///     it, rather than a report that always has something to say.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Without this, "the report names what is missing" is satisfied by a report that names
    ///     something for every row — which is the same silence with more words.
    /// </remarks>
    [Fact]
    public void WhenNothingWasDeclined_NoReasonIsReported()
    {
        var report = Generate("""
            namespace App.Billing.Reads
            {
                [Pragmatic.Endpoints.Attributes.Endpoint(Pragmatic.Endpoints.Attributes.HttpVerb.Get, "/api/invoices/{id}")]
                [Pragmatic.Authorization.RequirePermission("billing.invoice.read")]
                public class GetInvoiceEndpoint { }
            }
            """);

        report.Should().NotContain("it declares no permission");
        report.Should().NotContain("multipart");
        report.Should().Contain("[]),",
            "the row's list of reasons is empty, and it is the last argument of the entry");
    }

    private static string Generate(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(Preamble + source, new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create("CoverageGenTest", [tree], BaseReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ContractTestGenerator());

        return driver.RunGenerators(compilation).GetRunResult().GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains("_ContractCoverage"))?.GetText().ToString() ?? "";
    }

    private static MetadataReference[] BaseReferences() =>
    [
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(System.Guid).Assembly.Location),
    ];
}
