# Warehouse

Reference example 4 of Pragmatic.Design. Orders placed against a warehouse that answers at once,
fulfilled by a process that finishes when it finishes. Three services — **Orders**, **Stock** and
**Shipping** — run behind one gateway, with Stock running as **two instances** and a Pragmatic Agent
beside every host. Each service has its own database on one PostgreSQL; they talk over **RabbitMQ**, and
the two Stock instances share their cache invalidations over **Redis**.

It is the level the other three stop below. [Time off](../time-off/README.md) is one module;
[Invoicing](../invoicing/README.md) is two modules in one host; [Casework](../casework/README.md) is two
services that talk over a broker. Here several hosts are run as one application: routed and balanced by
a gateway that learns of them from their Agents, configured and switched at runtime on every instance,
and kept right when two instances of the same service write the same rows.

## Run it

```bash
docker compose up -d --wait     # from this folder: PostgreSQL, RabbitMQ, Redis
node run.mjs                    # the whole topology; prints the gateway and a token per role
```

`run.mjs` builds the Agent, the gateway and the three hosts, then starts what the suite starts
(`tests/…/Infrastructure/WarehouseFixture.cs`), on fixed ports:

| | |
|---|---|
| five Agents, one cluster | gossip on 7951–7955; the gateway's first, the others join it, one development `--gossip-key` |
| Orders | http://127.0.0.1:5221 |
| Stock, twice | http://127.0.0.1:5222 and :5223, started together as two replicas would be |
| Shipping | http://127.0.0.1:5224 |
| the gateway | **http://127.0.0.1:5220**, routes built from what its Agent holds |

Each host announces its route to the Agent beside it (`Pragmatic:Agent:Announce`), and the hosts and the
gateway share a development `Jwt:Key`. It prints a token for `order-desk`, `stock-clerk`,
`stock-manager` and `shipping-clerk`, then waits; Ctrl+C stops everything. The logs are in
`warehouse-run/logs` under the system temporary folder, and the script prints the path.

```bash
curl -i -H "Authorization: Bearer <stock-manager token>" http://127.0.0.1:5220/warehouse/health
```

Repeat it and `X-Served-By` alternates between the two Stock instances.

`node run.mjs --check` starts the same topology and proves it, then stops and exits 0 or 1:
- every service answers through the gateway;
- Stock accepts the token past the gateway (`GET /warehouse/api/products` as `stock-clerk`);
- both Stock instances answer;
- after one is stopped, twenty reads are all 200 and all from the other.

It passes from empty databases (`docker compose down -v`, then `up -d --wait`) and again on the
databases a previous run has migrated.

PostgreSQL answers on **5443**, with one database per service (`docker/create-databases.sql`); the
password in the hosts' `appsettings.json` is that local container's, set in `docker-compose.yml` — not a
secret, and not what a deployment uses. RabbitMQ is
on 5673 (management 15673), and the services talk over it. Redis is on 6380, and the Stock instances
broadcast cache invalidations over it. The ports are chosen so the four examples run at once. Each
service's API reference is at `/scalar` on its own port. `dotnet run --project src/<Host>` still starts
a single host, without an Agent or a gateway, provided `Jwt:Key` is set.

## Test it

```bash
node scripts/check.mjs --tier docker --only Warehouse.IntegrationTests   # from the repository root
```

**70 passed and none skipped in 1m 34s**, or **2m 16s** including the clean build the command pays for,
measured with the command above. Every test boots the whole application: PostgreSQL, RabbitMQ
and Redis in containers, **five Agent daemons** started as the processes they are in a deployment and
joined into one cluster, Orders, Stock twice and Shipping on real ports, and the real gateway in front of
them. No in-memory transport, no test double for the broker or the Agent, and no test-only
authentication: the suite signs its tokens with each host's own `JwtTokenGenerator`. Which instance
answered is read from `X-Served-By`, written by every host (`TheInstanceThatAnswered.cs`).

The whole docker tier, measured inside `node scripts/check.mjs --tier all` (a late run):
**20 suites, 3,003 passed and none skipped, in 16m 54s**. Warehouse was **1m 32s** of it, about 9%. A timing
from a late run is not comparable with one from the morning, because the machine drifts within a session
(`docs/TESTING.md` has the measurement). A delta is read between two consecutive runs, not against
this paragraph.

**All 70 are hand-written**, as 54 `[Fact]` and 5 `[Theory]` methods counted in the test sources.
`generated-code-stats.mjs` finds nothing generated into the test project by Pragmatic: the example does
not use `Pragmatic.Testing.SourceGenerator`, which Casework's contracts come from. None is skipped.

## What it demonstrates, and where to look

| | Where |
|---|---|
| **One gateway, routes nobody configured** — each host announces its route and address to its Agent; the Agents gossip it; the gateway builds its routes from what its own Agent holds | `Pragmatic.Agent.Client` (`Pragmatic:Agent:Announce`), `tests/…/HostsAnnounceThemselves.cs` |
| **A stopped instance leaves the rotation** — the announcement is ephemeral, deleted when the host's connection closes | `tests/…/HostsAnnounceThemselves.cs` |
| **The edge checks who, the service checks what** — the gateway validates the token and refuses without it; the service still refuses a role that holds nothing there | `tests/…/TheGatewayIsTheOnlyDoor.cs` |
| **Both instances answer** — the gateway takes Stock's two instances in turn | `tests/…/TheThreeServicesAnswerThroughTheGateway.cs` |
| **A release without a failed request** — one Stock instance drained through its Agent leaves the rotation and stays alive, what it had in flight completes, and it is put back afterwards | `tests/…/DrainingOneInstanceFailsNoRequest.cs` |
| **Every host beside its Agent** — the connection read from the host's side, and the roster that lists each host by its own name | `tests/…/EveryHostHasAnAgentBesideIt.cs` |
| **One cache truth across two instances** — `[Cacheable]` availability, invalidated on both over Redis | `Warehouse.Stock.Host/Program.cs` (`AddRedisCacheInvalidationBroadcast`), `tests/…/TwoInstancesAgreeOnAvailability.cs` |
| **A setting and a switch changed at runtime** — the reorder threshold and `AcceptBackorders`, written through the Agent, seen by both instances without a restart | `Warehouse.Stock/Infrastructure/Configuration/ReorderOptions.cs`, `…/FeatureFlags/AcceptBackordersFlag.cs`, `tests/…/ASettingAndASwitchChangeOnEveryInstance.cs` |
| **An answer inside the caller's request, over the broker** — placing an order reserves its stock while the customer waits, all or nothing | `Warehouse.Orders/Actions/PlaceOrderAction.cs`, `Warehouse.Stock/Infrastructure/RequestHandlers/AnswerReservationRequests.cs`, `tests/…/PlacingAnOrderHoldsItsStock.cs` |
| **A hold nobody confirms** — a durable job in Stock's database gives the stock back | `Warehouse.Stock/Infrastructure/Jobs/ExpireReservationsJob.cs`, `tests/…/AHoldNobodyConfirmsGivesItsStockBack.cs` |
| **A process across three services** — the fulfilment saga in Orders, which Stock and Shipping do not know exists | `Warehouse.Orders/Infrastructure/Sagas/OrderFulfilmentSaga.cs`, `tests/…/AnOrderTravelsToItsCarrier.cs`, `…/APickedOrderBecomesAShipment.cs` |
| **Cancelling compensates** — each service undoes its own part from its own state, and the process is `Compensated` when every one has answered | `tests/…/CancellingUndoesWhatTheOthersDid.cs` |
| **An order moves only along its machine** | `Warehouse.Orders/Enums/OrderStatus.cs`, `tests/…/AnOrderMovesOnlyAsItsMachineAllows.cs` |
| **Stock changes only by a movement**, and a catalogue in two languages | `Warehouse.Stock/Levels/StockLevel.cs`, `tests/…/TheStockChangesOnlyByAMovement.cs`, `…/TheCatalogueThroughTheGateway.cs` |
| **A long job in parts, with progress someone watches** — a supplier's file as a batch; parts done, failed and in total, and every refused row with its reason | `Warehouse.Stock/Imports/`, `tests/…/ASupplierFileArrivesInParts.cs` |

## Why it is built this way

Two threads hold the example together.

### Several hosts, run as one

**Two Stock instances, because one of each would make the rest decoration.** With one instance per
service, a gateway is a reverse proxy, an Agent is a daemon nobody reads, and a cache cannot be wrong.
Two instances of Stock are what make the gateway balance, the Agents announce, the cache be invalidated
across processes, and a level be written by two writers at once. Stock is the one run twice because it
is read the most, and both the orders and the imports write to it.

**The gateway is the door.** `Pragmatic.Gateway` publishes each service under its prefix — `/orders`,
`/warehouse`, `/shipping` — and removes it on the way in; the Stock route has both instances and takes
them in turn. No route is configured on the gateway. Each host announces its own to the Agent beside it
(`Pragmatic:Agent:Announce`), the Agents gossip it as one cluster, and the gateway's Agent tells the
gateway. The announcement lives as long as the host's connection: stop one Stock instance and, within
seconds, every read is the other's. Every route requires a token, validated at the edge against the key,
issuer and audience the services share: a request without one is the gateway's 401 and no service sees
it. The token is forwarded, and each service still decides what it allows — a valid token whose role
holds nothing there is the service's 403. The Stock cluster has its own timeout and circuit breaker in
the gateway's configuration.

**Redis carries invalidations, not values.** Availability is `[Cacheable]`, and each Stock instance keeps
its own copy. A movement drops the copy of the instance that made it; `AddRedisCacheInvalidationBroadcast`
carries that invalidation to the other instance. Without it, measured, a receipt on one instance is not
what the other reads next — it answers what it cached, until the entry expires. Redis is here for that
and nothing else: each instance reads the database again for its own copy.

The entries are tagged per product, and a movement drops the products it moved — a receipt one product,
an order's hold or pick the products on its lines — and the read without a product filter, which covers
them all. One tag for every product made each movement empty the whole cache on both instances, the
expiry job of an order confirmed in time included, which gives back nothing (`Infrastructure/Caching`).

**A setting and a switch, changed while it runs.** Two things change at runtime on both Stock instances,
written through the Agent and never in a file:

- `config/Warehouse:DefaultReorderThreshold` is the reorder threshold of a product that has none of its
  own. `UseAgent()` puts the Agent's `config/` keys into the host's configuration, so it binds to
  `ReorderOptions` like an appsettings value. `ListProductsToReorderAction` (`GET api/products/to-reorder`)
  reads it through `IOptionsMonitor`, which sees the change within a gossip round. `IOptions` is read
  once, at startup, and would not.
- `flags/AcceptBackorders` is the `AcceptBackordersFlag` switch. Off, an order short of stock is refused
  whole (409). On, Stock holds what there is and backorders the rest of each line, and the order line says
  how many (`backordered`). The switch is read at each request, so whichever Stock instance takes the
  reservation answers the same way.

**Two writers, one shelf.** Two instances apply two parts of a supplier's file at once, onto the same
levels. `StockLevel` is `[ConcurrencyAware]`, so the second save fails instead of silently undoing the
first receipt. Measured without it: 241 on hand where the file said 397. A part meeting another writer
is applied again, whole, in a fresh scope.

### Now and eventually

**The one request that waits for an answer.** Placing an order cannot finish until its stock is known to
be held: an order accepted and then found short is a promise taken back. So `PlaceOrderAction` asks
Stock with `IMessageBus.RequestAsync` and answers the customer with the outcome — `Reserved`, a 409 naming
the short lines with nothing held for any of them, or a 503 when Stock does not answer within
`Messaging:RequestReplyTimeout`.

**Why over the broker, and HTTP elsewhere.** A `RemoteBoundary`, the framework's default for one service
calling another over HTTP, would need Orders to know where Stock is. On the broker it does not: the
request goes to the queue `requests.reserve-stock`, both Stock instances consume it, and whichever is up
answers. Everywhere else a service tells another what happened and does not wait, as in Casework. The
two shapes sit side by side here so the reason for each is visible: the customer waits for the
reservation, and nobody waits for the carrier.

**A hold nobody confirms.** A hold lasts `Reservations:HoldSeconds` (thirty minutes unless configured).
When Stock holds an order's stock it also schedules `ExpireReservationsJob` for that moment, in the same
transaction: the job is a row in Stock's database (`[EnableJobPersistence]`), not a timer in the instance
that took the request, so it runs even if every Stock instance was down when it was due. It gives back
whatever is still held and publishes `ReservationExpired` through the outbox; Orders cancels the order
with the reason "reservation expired" — only a `Reserved` one, so a repeated message, or an order
confirmed just in time, changes nothing.

**From confirmation to carrier.** `OrderFulfilmentSaga` lives in Orders, and Stock and Shipping do not
know it exists. It starts on `OrderReadyToPick`; the stock clerk picks the order (`POST api/picks`), which
turns the holds into pick movements and publishes `OrderPicked`; Shipping makes the shipment and, when the
shipping clerk dispatches it, publishes `ShipmentDispatched`. The saga answers each of the two with a
message to Orders' own handlers, which move the order to `Picked` and `Shipped` through its machine. Its
state is rows in Orders' database, so a restart in the middle resumes it.

**Cancelling before it ships.** An order can be cancelled until it is `Shipped`. The cancellation raises
`OrderCancelled`, and each service undoes its own part from its own state — Stock releases what it still
holds and returns what it picked to its shelf, once per order; Shipping withdraws a shipment that has not
left. Each says so, and the process is `Compensated` only when every service that had done something has
answered. One fact, each service reading its own database, rather than a command per service chosen by
Orders, which would need Orders to know what the others did.

**A supplier's file, in parts.** `POST api/imports` takes a CSV (`sku,location,quantity`), checks its
header, and cuts its rows into parts. `IBatchDispatcher` publishes them on the broker as one batch, so
both Stock instances apply parts at once. Each part is one transaction: valid rows become receipts,
invalid ones are recorded with their line and reason, and the part is recorded once, so a part delivered
twice changes nothing. `GET api/imports/{id}` answers parts done, failed and in total from the progress
table the boundary declares (`[EnableBatchProgress]`). A part with refused rows is failed without
throwing (`BatchItemOutcome.Fail()`): a throw would roll back its valid rows, and the transport nacks a
failed message without requeue.

## The numbers, measured

Measured with `node scripts/generated-code-stats.mjs warehouse`, which rebuilds each
project with its generated files written to a folder emptied first. A line counts when it is neither
blank nor a comment; the 4,207 lines of generated-file banner are left out of every count. The full
per-file report is `artifacts/generated-code-stats/report.json`.

| | Files | Lines |
|---|---:|---:|
| Written — `src/Warehouse.Orders` | 29 | 414 |
| Written — `src/Warehouse.Orders.Contracts` | 2 | 10 |
| Written — `src/Warehouse.Orders.Host` | 5 | 76 |
| Written — `src/Warehouse.Stock` | 59 | 1,115 |
| Written — `src/Warehouse.Stock.Contracts` | 8 | 28 |
| Written — `src/Warehouse.Stock.Host` | 5 | 93 |
| Written — `src/Warehouse.Shipping` | 16 | 206 |
| Written — `src/Warehouse.Shipping.Contracts` | 2 | 11 |
| Written — `src/Warehouse.Shipping.Host` | 5 | 72 |
| **Generated by Pragmatic — `Warehouse.Orders`** | 115 | 5,297 |
| **Generated by Pragmatic — `Warehouse.Orders.Contracts`** | 7 | 141 |
| **Generated by Pragmatic — `Warehouse.Orders.Host`** | 20 | 1,664 |
| **Generated by Pragmatic — `Warehouse.Stock`** | 212 | 11,593 |
| **Generated by Pragmatic — `Warehouse.Stock.Contracts`** | 7 | 174 |
| **Generated by Pragmatic — `Warehouse.Stock.Host`** | 27 | 2,883 |
| **Generated by Pragmatic — `Warehouse.Shipping`** | 77 | 3,185 |
| **Generated by Pragmatic — `Warehouse.Shipping.Contracts`** | 7 | 150 |
| **Generated by Pragmatic — `Warehouse.Shipping.Host`** | 20 | 1,302 |
| Written — `tests/Warehouse.IntegrationTests` | 27 | 2,057 |

**2,025 lines of application** in 131 files, against **26,389 generated by Pragmatic** in 492 — **13.0
times as much**. The three hosts' fifteen files are 241 lines of composition for three services, a
broker, a cache, an Agent each and a database each. Eight more files and 1,561 lines come from generators
that are not ours (`LoggerMessage`, ASP.NET's OpenAPI XML-comment helper, `PublicProgram`) and are counted
apart. The largest single artefact Pragmatic wrote is the **repositories** (13 files, 4,125 lines, 15.6% of
its code), then the hosts' OpenAPI documents (8.7%).

**What it moved.** Two measures, each a command:

- `node scripts/reference-app-packages.mjs` counts the runtime packages the reference applications reach
  through their project references. Of **140**, the other three reach **78**
  (`node scripts/reference-app-packages.mjs time-off invoicing casework`), and the four reach **86**.
  The eight this one adds are `Agent`, `Agent.Client`, `Agent.Protocol`, `Caching.Redis`,
  `Configuration`, `FeatureFlags`, `Gateway` and `Messaging.Batch`. `Agent.Discovery` is reached by
  the Showcase alone: the hosts find each other through the gateway's routes, not through discovery.
- `node scripts/example-coverage.mjs` counts the attributes an example writes: **259 of 283**, against
  **256** without this example. It is the one that writes `[RequestHandler]`, `[EnableBatchProgress]`
  and the `Pragmatic.Messaging.Batch` package, so the coverage register does not list them as unused.

To read the generated code, build once and open `src/Warehouse.Stock/obj/Debug/net10.0/generated/`.
⚠️ That folder is never emptied, so a name read from it is not evidence that the generator still writes
it, which is why the table above comes from `generated-code-stats.mjs`. Good places to start:

- `Warehouse.Stock.Reservations.Actions.ReserveStockAction.Invoker.g.cs` — the pipeline an action runs in
- `Warehouse.Orders.Infrastructure.Sagas.OrderFulfilmentSaga.Orchestrator.g.cs` — the saga, as declared

## What it is not

- **No UI**, no client and no deployment. `run.mjs` starts the topology on one machine for development.
- **One tenant.** Multi-tenancy is Invoicing's and Casework's.
- **No identity provider**: the services sign their own tokens and store no account.
- **No exactly-once.** The transport is at-least-once, and what makes a repeat harmless is each service's
  own record of what it did — a hold per order, a restoration per order, a part per import.

## Where the capabilities it uses are documented

[Gateway](../../Pragmatic.Gateway/README.md) ·
[Agent](../../Pragmatic.Agent/README.md) ·
[Messaging](../../Pragmatic.Messaging/README.md) ·
[Caching](../../Pragmatic.Caching/README.md) ·
[Configuration](../../Pragmatic.Configuration/README.md) ·
[FeatureFlags](../../Pragmatic.FeatureFlags/README.md) ·
[Actions](../../Pragmatic.Actions/README.md) ·
[Endpoints](../../Pragmatic.Endpoints/README.md) ·
[Persistence](../../Pragmatic.Persistence/README.md) ·
[Jobs](../../Pragmatic.Jobs/README.md) ·
[Documents](../../Pragmatic.Documents/README.md) ·
[Internationalization](../../Pragmatic.Internationalization/README.md) ·
[Identity](../../Pragmatic.Identity/README.md) ·
[Authorization](../../Pragmatic.Authorization/README.md)
