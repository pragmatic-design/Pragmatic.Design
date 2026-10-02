using System.Text;
using Testcontainers.ServiceBus;

namespace Pragmatic.Messaging.Tests.Integration;

/// <summary>
///     Starts the official Azure Service Bus EMULATOR (plus its SQL companion) via
///     Testcontainers. The emulator does not support the management API, so the entities the
///     tests use are pre-provisioned through the emulator's Config.json — exactly the
///     pass-through scenario <c>AzureServiceBusOptions.AutoCreateEntities</c> degrades to.
///     When Docker is unavailable the fixture degrades gracefully (ConnectionString null).
/// </summary>
public sealed class AzureServiceBusContainerFixture : IAsyncLifetime
{
    /// <summary>Entities provisioned in the emulator for the integration tests.</summary>
    public const string Topic = "asb-events";
    public const string Subscription = "asb-sub";
    public const string Queue = "asb-p2p";

    /// <summary>Request/reply queues (the emulator cannot create entities at runtime).</summary>
    public const string RequestQueue = "requests.asb-quote-request"; // = RequestReplyConventions.QueueFor(typeof(AsbQuoteRequest))
    public const string ReplyQueue = "replies.asb-tests";

    private const string EmulatorConfig = /*lang=json*/ """
        {
          "UserConfig": {
            "Namespaces": [
              {
                "Name": "sbemulatorns",
                "Queues": [
                  {
                    "Name": "asb-p2p",
                    "Properties": { "MaxDeliveryCount": 5, "DeadLetteringOnMessageExpiration": true }
                  },
                  { "Name": "requests.asb-quote-request", "Properties": { "MaxDeliveryCount": 5 } },
                  { "Name": "replies.asb-tests", "Properties": { "MaxDeliveryCount": 5 } }
                ],
                "Topics": [
                  {
                    "Name": "asb-events",
                    "Subscriptions": [
                      {
                        "Name": "asb-sub",
                        "Properties": { "MaxDeliveryCount": 5, "DeadLetteringOnMessageExpiration": true }
                      }
                    ]
                  }
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
        catch
        {
            // Docker not available — tests skip gracefully.
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
