# Examples — pragmatic-use-messaging

Copied from `examples/warehouse/src`, which compiles in the repository and is exercised by
`examples/warehouse/tests/Warehouse.IntegrationTests`. **Do not edit here**: change the source and run
`node scripts/sync-skill-examples.mjs`. The gate refuses a copy that differs from its source.

| File | What it shows |
|---|---|
| [`Warehouse.Orders.Contracts/Events/OrderReadyToPick.cs`](Warehouse.Orders.Contracts/Events/OrderReadyToPick.cs) | An integration event in the publisher's contracts: a `DomainEvent` that is also `IIntegrationEvent`, with the `[CorrelationKey]` a saga starts on |
| [`Warehouse.Orders/Enums/OrderStatus.cs`](Warehouse.Orders/Enums/OrderStatus.cs) | Raised by the move that confirms the order (`[RaisesEvent<T>]`), so the outbox captures it in the same transaction |
| [`Warehouse.Orders/OrdersBoundary.cs`](Warehouse.Orders/OrdersBoundary.cs) | `[EnableOutbox]` and `[EnableSagaPersistence]` on the publishing boundary |
| [`Warehouse.Orders.Host/Program.cs`](Warehouse.Orders.Host/Program.cs) | RabbitMQ, the outbox's polling interval, and the contracts registry the outbox needs (PRAG1699) |
| [`Warehouse.Shipping/Infrastructure/MessageHandlers/MakeAShipmentOfAPickedOrder.cs`](Warehouse.Shipping/Infrastructure/MessageHandlers/MakeAShipmentOfAPickedOrder.cs) | A `[MessageHandler]` in the consuming service: idempotent on the event's id, through the internal interface, throwing so a failure is redelivered |
| [`Warehouse.Orders/Infrastructure/Sagas/OrderFulfilmentSaga.cs`](Warehouse.Orders/Infrastructure/Sagas/OrderFulfilmentSaga.cs) | A saga in the service that owns the process: `[Saga<TState>]`, `[SagaStart]`, `[InState]` steps answering with commands, and a compensation that waits for every acknowledgement |
| [`Warehouse.Stock/Infrastructure/MessageHandlers/ApplyImportParts.cs`](Warehouse.Stock/Infrastructure/MessageHandlers/ApplyImportParts.cs) | Batch fan-out: a part failed through `BatchItemOutcome` rather than by throwing, and a part that met another writer applied again in a fresh scope |
| [`Warehouse.Stock.Host/Program.cs`](Warehouse.Stock.Host/Program.cs) | `EnableBatchProcessing()` with the application's `IBatchSplitter`, next to the outbox and the broker |
