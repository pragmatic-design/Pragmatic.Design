using Pragmatic.Testing.Assertions;
using Pragmatic.Migrations.Cli.ClientGen;

namespace Pragmatic.Migrations.Core.Tests.Unit.Cli;

/// <summary>
///     Unit tests for <see cref="CSharpClientGenerator"/>. The generator writes files to disk, so
///     each test uses an isolated temp directory and asserts on the emitted source text. No network
///     or compilation is required — these verify the generated string shapes only.
/// </summary>
public class CSharpClientGeneratorTests : IDisposable
{
    private readonly string _outDir =
        Path.Combine(Path.GetTempPath(), "pragmatic-csgen-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_outDir))
            Directory.Delete(_outDir, recursive: true);
    }

    private string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { _outDir }.Concat(parts).ToArray()));

    [Fact]
    public void Generate_GetEndpoint_ProducesResultReturningInterfaceAndClient()
    {
        var manifest = new ClientManifest
        {
            Assembly = "Acme.Billing",
            Endpoints =
            [
                new ClientEndpoint
                {
                    OperationId = "Billing.GetInvoice",
                    HttpMethod = "GET",
                    FullRoute = "/invoices/{id}",
                    IsVoid = false,
                    Response = new ClientResponse { Type = "System.Guid" },
                    Parameters = [new ClientParam { Name = "id", In = "path", Type = "System.Guid" }],
                }
            ],
        };

        new CSharpClientGenerator(manifest, "Acme.Billing.Client", _outDir).Generate();

        var iface = Read("IBillingClient.cs");
        iface.Should().Contain("public interface IBillingClient");
        iface.Should().Contain("Task<Result<Guid, IError>> GetInvoice(Guid id, CancellationToken ct = default);");

        var client = Read("BillingHttpClient.cs");
        client.Should().Contain("http.GetAsync");
        // Result-over-exceptions: errors are mapped, never thrown.
        client.Should().Contain("MapErrorAsync");
        client.Should().Contain("ProblemDetailsPayload");
    }

    [Fact]
    public void Generate_VoidPostEndpoint_ReturnsVoidResult()
    {
        var manifest = new ClientManifest
        {
            Assembly = "Acme.Billing",
            Endpoints =
            [
                new ClientEndpoint
                {
                    OperationId = "Billing.CancelInvoice",
                    HttpMethod = "POST",
                    FullRoute = "/invoices/cancel",
                    IsVoid = true,
                    RequestBody = new ClientBody
                    {
                        Properties = [new ClientProperty { Name = "Id", Type = "System.Guid" }],
                    },
                }
            ],
        };

        new CSharpClientGenerator(manifest, "Acme.Billing.Client", _outDir).Generate();

        var client = Read("BillingHttpClient.cs");
        client.Should().Contain("VoidResult<IError>");
        client.Should().Contain("PostAsJsonAsync");
        client.Should().Contain("return VoidResult<IError>.Success();");
    }

    [Fact]
    public void Generate_AlwaysEmitsHttpRequestErrorAndRegistration()
    {
        var manifest = new ClientManifest { Assembly = "Acme.Billing", Endpoints = [] };

        new CSharpClientGenerator(manifest, "Acme.Billing.Client", _outDir).Generate();

        var httpError = Read("Errors", "HttpRequestError.cs");
        httpError.Should().Contain("public sealed record HttpRequestError");
        httpError.Should().Contain(": IError");

        var registration = Read("BillingClientExtensions.cs");
        registration.Should().Contain("public static IServiceCollection AddBillingClient(");
        registration.Should().Contain("AddHttpClient<IBillingClient, BillingHttpClient>");
    }

    [Fact]
    public void Generate_EntityType_ProducesDtoRecord()
    {
        var manifest = new ClientManifest
        {
            Assembly = "Acme.Billing",
            Types =
            [
                new ClientType
                {
                    SimpleName = "Invoice",
                    Kind = "entity",
                    Properties =
                    [
                        new ClientProperty { Name = "Id", Type = "System.Guid" },
                        new ClientProperty { Name = "Note", Type = "System.String", IsNullable = true },
                    ],
                }
            ],
        };

        new CSharpClientGenerator(manifest, "Acme.Billing.Client", _outDir).Generate();

        var dto = Read("Models", "InvoiceDto.cs");
        dto.Should().Contain("public sealed record InvoiceDto");
        dto.Should().Contain("public Guid Id { get; init; }");
        dto.Should().Contain("public string? Note { get; init; }");
    }
}
