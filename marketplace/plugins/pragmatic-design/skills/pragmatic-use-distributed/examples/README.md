# Examples: pragmatic-use-distributed

Copied from `examples`, which compiles in the repository and is exercised by
`examples/warehouse/tests/Warehouse.IntegrationTests` and `examples/showcase/tests/Showcase.Billing.Host.Tests`. **Do not edit here**: change the source and run
`node scripts/sync-skill-examples.mjs`. The gate refuses a copy that differs from its source.

| File | What it shows |
|---|---|
| [`warehouse/src/Warehouse.Orders.Contracts/Warehouse.Orders.Contracts.csproj`](warehouse/src/Warehouse.Orders.Contracts/Warehouse.Orders.Contracts.csproj) | A contracts project: Abstractions, Events and Messaging.Core, nothing else; all a consumer references of the service |
| [`warehouse/src/Warehouse.Stock.Contracts/ReserveStock.cs`](warehouse/src/Warehouse.Stock.Contracts/ReserveStock.cs) | A request in the answering service's contracts: the one type both sides name |
| [`warehouse/src/Warehouse.Orders/Actions/PlaceOrderAction.cs`](warehouse/src/Warehouse.Orders/Actions/PlaceOrderAction.cs) | Request/reply over the broker from an action: `IMessageBus.RequestAsync`, a short answer as 409 and no answer as 503, nothing saved either way |
| [`warehouse/src/Warehouse.Stock/Infrastructure/RequestHandlers/AnswerReservationRequests.cs`](warehouse/src/Warehouse.Stock/Infrastructure/RequestHandlers/AnswerReservationRequests.cs) | The `[RequestHandler]` answering it, from whichever instance takes the request off the shared queue |
| [`warehouse/src/Warehouse.Orders.Host/Program.cs`](warehouse/src/Warehouse.Orders.Host/Program.cs) | A service's host: RabbitMQ, the outbox, the contracts registry (PRAG1699), the request timeout, JWT and the Agent |
| [`warehouse/src/Warehouse.Stock.Host/TheInstanceThatAnswered.cs`](warehouse/src/Warehouse.Stock.Host/TheInstanceThatAnswered.cs) | An `IStartupStep` that writes `X-Served-By` on every response, so a test can tell which instance answered |
| [`showcase/src/Showcase.Billing.Host/BillingHostModule.cs`](showcase/src/Showcase.Billing.Host/BillingHostModule.cs) | `[RemoteBoundary<BookingModule>]`: a host reaching another module over HTTP behind the same interface |
