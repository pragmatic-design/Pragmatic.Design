using System.Text;
using Testcontainers.ServiceBus;

namespace Showcase.IntegrationTests.Infrastructure;

/// <summary>
///     Starts the official Azure Service Bus EMULATOR (Testcontainers) pre-provisioned with the
///     Showcase topology: one <c>{boundary}.events</c> topic per boundary plus the subscriptions
///     the transport binder creates for the Showcase message handlers
///     (<c>{module}.{message-kebab}</c>). The emulator has no management API, so the app
///     runs with <c>AutoCreateEntities = false</c> — the same pass-through mode an
///     IaC-provisioned production namespace uses. Degrades gracefully without Docker.
///     ⚠️ Emulator quirk: every topic REQUIRES at least one subscription (empty
///     <c>Subscriptions: []</c> crashes the emulator host at startup, exit 139) — topics that
///     only absorb outbox traffic get a "drain" subscription.
/// </summary>
/// <remarks>
///     ⚠️ <b>These names are pinned twice</b> — here and by the naming convention the binder uses — and
///     a rename on one side is a test that times out waiting for a message the broker delivered
///     nowhere. This fixture is the smallest possible demonstration of why a subscription name is
///     <b>operational state</b>: change it and the entity provisioned under the old name keeps existing, bound, and
///     unread. An IaC-provisioned namespace behaves exactly the same way, which is the point of running
///     the emulator in pass-through mode.
/// </remarks>
public sealed class AzureServiceBusEmulatorFixture : IAsyncLifetime
{
    private const string EmulatorConfig = /*lang=json*/ """
        {
          "UserConfig": {
            "Namespaces": [
              {
                "Name": "sbemulatorns",
                "Queues": [],
                "Topics": [
                  {
                    "Name": "booking.events",
                    "Subscriptions": [
                      { "Name": "billing.reservation-confirmed", "Properties": { "MaxDeliveryCount": 5 } }
                    ]
                  },
                  {
                    "Name": "billing.events",
                    "Subscriptions": [
                      { "Name": "billing.invoice-paid", "Properties": { "MaxDeliveryCount": 5 } }
                    ]
                  },
                  { "Name": "catalog.events", "Subscriptions": [ { "Name": "drain" } ] },
                  { "Name": "accounts.events", "Subscriptions": [ { "Name": "drain" } ] },
                  { "Name": "identity.events", "Subscriptions": [ { "Name": "drain" } ] }
                ]
              }
            ],
            "Logging": { "Type": "File" }
          }
        }
        """;

    private ServiceBusContainer? _container;

    /// <summary>Emulator connection string, or null when Docker is unavailable.</summary>
    public string? ConnectionString { get; private set; }

#pragma warning disable CA2007 // xUnit manages SynchronizationContext
    public async Task InitializeAsync()
    {
        try
        {
            _container = new ServiceBusBuilder()
                .WithAcceptLicenseAgreement(true)
                .WithResourceMapping(
                    Encoding.UTF8.GetBytes(EmulatorConfig),
                    "/ServiceBus_Emulator/ConfigFiles/Config.json")
                .Build();
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
        }
        catch (Exception ex)
        {
            // Docker not available — tests skip gracefully.
            Console.WriteLine($"[AzureServiceBusEmulatorFixture] emulator unavailable: {ex}");
            ConnectionString = null;
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
#pragma warning restore CA2007
}
