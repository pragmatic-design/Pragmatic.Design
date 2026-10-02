# Invoicing

Reference example 2 of Pragmatic.Design. An invoicing service several companies use at once: each one
keeps its customers, drafts and issues invoices, records what was paid, and has its overdue invoices
chased for it by e-mail every morning. It is two modules in one host on one PostgreSQL —
`Invoicing.Registry` (companies and customers) and `Invoicing.Billing` (invoices and payments) — where
every row belongs to a company and the company comes from the caller's token, signed by that company's
own OpenID Connect provider, because Invoicing issues no token and holds no password. An issued invoice
becomes a PDF kept as it was made, and the application speaks English and Italian.

## Run it

```bash
docker compose up -d --wait
dotnet run --project src/Invoicing.Host
```

The API reference opens at <http://localhost:5190/scalar> and the OpenAPI document is at
<http://localhost:5190/openapi/v1.json>. The migration runs at startup and creates the schema from the
entities; the data is disposable, so `docker compose down -v` and up again is the way back from a
schema change the migration refuses. The password in `appsettings.json` is that local container's, set in
`docker-compose.yml`: not a secret, and not what a deployment uses.

**Without an identity provider no route answers anything useful**, and that is the design rather than a
missing piece: the caller is whoever a provider says they are. What comes back is **400** on the 19
routes that belong to a company — tenant resolution runs before authorization, and a request with no
token resolves no tenant — and **401** on the three that belong to none (`api/organizations/*`). Point
`appsettings.Development.json` at a provider —

```json
{
  "Oidc": {
    "Authority": "http://localhost:8080/realms/invoicing",
    "Audience": "invoicing-api"
  }
}
```

— and mint a token whose `sub` is the user, whose `roles` claim is a JSON array holding one of
`viewer`, `accountant`, `platform-administrator`, and whose `tenant_id` claim is the company's slug.
`Audience` is mandatory outside Development: left empty the handler would accept a token minted for any
other client of the same provider.

**The tests need no provider.** They sign their own tokens with a key the handler is given
(`tests/Invoicing.IntegrationTests/Infrastructure/TestIdentityProvider.cs`), so issuer, audience,
lifetime and signature are all validated for real, against no network.

## Test it

```bash
node scripts/check.mjs --tier docker --only Invoicing.IntegrationTests   # from the repository root
```

**166 passed and none skipped**, in 22–30 seconds. ⚠️ Consecutive runs have read 22.3s, 22.3s, 23.2s
and 32.5s — the last one while other container suites were competing for the same Docker — so the count
is the number to rely on and the duration is the machine's. A single decimal quoted from one run would
be a precision this suite does not have. Each test boots the real host and talks HTTP to it:
PostgreSQL in a container, a signed token as a client would send, no test-only authentication anywhere.
The suite is part of the gate's docker tier — `<RequiresDocker>true</RequiresDocker>` is the whole
registration — and CI runs that tier on ubuntu.

**111 of those 166 tests are hand-written and 55 are generated** — the contract tests
`Pragmatic.Testing.SourceGenerator` writes from the endpoints (24 + 19 authorization contracts for the
two boundaries, 3 + 3 CRUD, 6 state-transition), counted as the `FactAttribute` occurrences in
`obj/Debug/net10.0/generated/Pragmatic.Testing.SourceGenerator/` **after a rebuild** — an incremental
build leaves generated files from the previous compilation in place, so counting those counts a
mixture. Among the generated ones is
`CreateCreateCustomerMutation_IsNotVisibleToAnotherTenant`, the one that fails when tenant resolution
is misconfigured. They run against the real OIDC identity because
`PragmaticContractHost.PrepareRequest` swaps the header identity they invent for a signed token
(`Infrastructure/ContractAppFixture.cs`).

⚠️ **This paragraph is checked by four of the hand-written tests** — they read this file and compare
the counts it quotes with the ones the application produces, so it fails a run when it goes stale
(`TheReadmeQuotesWhatTheSuiteMeasures`). Not the duration, which is the machine's, and not
the total, which xUnit does not hand a test.

## What it demonstrates, and where to look

| | Where |
|---|---|
| **Two modules, one direction** — Billing reads Registry through a published contract and never its entities | `Registry/Customers/Queries/GetCustomerBillingDetailsQuery.cs` (`[Published]` → `IRegistryReads`), `Billing/Invoices/Actions/IssueInvoiceAction.cs` |
| **Multi-tenancy from the token** — the tenant is a claim, the store is the companies table, four guards | `Host/Program.cs` (`UseMultiTenancy(t => t.UseClaim())`), `Registry/Infrastructure/MultiTenancy/OrganizationTenantStore.cs` |
| **OIDC** — an external provider, roles in the token, permissions from the roles | `Host/Program.cs` (`UseOidcAuthentication`), `Billing/Infrastructure/Authorization/` |
| **The invoice as a document** — one model, rendered to PDF once and stored per company and year | `Billing/Invoices/InvoiceDocument.cs`, `Billing/Invoices/InvoiceStorageKey.cs` |
| **Money** — an amount carries its currency, and both columns stay queryable | `Billing/Invoices/Invoice.cs`, `Billing/Invoices/InvoiceLine.cs` |
| **A number that cannot have holes** — a row per company and year, taken under a concurrency check | `Billing/Invoices/InvoiceNumberSeries.cs` |
| **A recurring job that fans out over the tenants** — and declines rather than counting to zero | `Billing/Infrastructure/Jobs/ChaseOverdueInvoicesJob.cs`, `…/OverdueReminderSweep.cs` (`[Service<T>]`, though three of its dependencies are registered outside this module) |
| **A durable job store** — the schedule and the queue are rows of this application's database, created by its migration | `Billing/BillingBoundary.cs` (`[EnableJobPersistence]`), `Host/Program.cs` (`jobs.UseEfCore()` + `UseEfCorePersistence()`) |
| **E-mail** — built from keys, in the customer's language, with the stored PDF attached | `Billing/Infrastructure/Email/OverdueReminder.cs` |
| **Declared reads** — filters, a search box, a `[ComputedFilter]` fed by the clock, a `GROUP BY` in SQL | `Billing/Invoices/Queries/` |
| **Two languages** — the refusal in the caller's, the document in the recipient's; every message a key, an aggregate rule's included | `*/translations/`, `Host/Program.cs` (`UseI18N`), `Billing/Invoices/InvoiceLine.cs` (`[Invariant(… MessageKey = …)]`) |
| **A state machine with a move back** — `Paid → Issued` when a payment is reversed; each move declares its target, so the contract tests are generated from it | `Billing/Enums/InvoiceStatus.cs`, `…/Actions/IssueInvoiceAction.cs` (`[TransitionsTo]`) |
| **Three meanings of "remove"** — a draft deleted, an invoice voided, a customer soft-deleted | `Billing/Invoices/Mutations/DeleteDraftInvoiceMutation.cs`, `…/Actions/VoidInvoiceAction.cs`, `Registry/Customers/Mutations/DeleteCustomerMutation.cs` |
| **The audit trail** — declared with one attribute, read back in a test | `Billing/Invoices/Invoice.cs` (`[Audited]`), `tests/…/RemovingWhatIsNoLongerWanted.cs` |

## Why it is built this way

**The tenant comes from the token, never from a header.** `tenancy.UseClaim()` reads `tenant_id` off
the validated principal. A header — or a route segment, or a subdomain — is input the client controls,
so an authenticated caller could name another company and be served its rows: that is the escalation
`EnforceTenantClaim` exists to close. The price is that a request with no token resolves no tenant and
is refused with **400** before authorization ever runs, which is why the anonymous-caller tests assert
401 only on the routes that belong to no tenant.

**The invoice number is a row, not a `[GeneratedValue]`.** A generated value draws from one sequence
for the whole table, so two companies issuing on the same day would get 1, 4, 9 and 2, 3, 5 — and an
invoice number has to be consecutive *within the issuer*. `InvoiceNumberSeries` is a row per company
and year, incremented under `[ConcurrencyAware]`; two issues racing make one of them fail the check and
come back 409 for the caller to retry. The number is taken **after** every refusal the operation can
give, because a rejected issue that had already consumed one would leave a hole nobody could explain
afterwards — and for the same reason voiding an invoice does not release its number.

**The customer is copied onto the invoice at issue, not joined.** Name, VAT number, address and
language are frozen on the row. A document already sent says what it said, and a customer who moves
office next month does not rewrite last year's invoices. It is also what lets a customer be deleted at
all: Registry cannot ask Billing whether an invoice references them without reversing the one
dependency between the two modules, and the snapshot is what makes the permissive answer safe.

**The PDF is rendered once and stored, never re-rendered.** The bytes are made when the invoice is
issued, put in the company's own container, and their SHA-256 is recorded on the invoice; the download
serves the file and the reminder attaches it. A second rendering would be a second document for one
invoice, and a customer could hold two that differ — which is why the test compares the attachment's
hash with the one the issue recorded rather than checking that a PDF came back.

## The numbers, measured

Measured with `node scripts/generated-code-stats.mjs invoicing`, which clears
`obj/**/generated` and rebuilds before counting (`obj/` keeps files from earlier builds, and a stale
one is not evidence). A line counts when it is neither blank nor a comment — trimmed first, dropped
when it then starts with `//` or `/*` or `*`.
⚠️ Trimming matters here: every generated file opens with a byte-order mark and `// <auto-generated/>`,
so a count that does not strip the mark reads that line as code and comes out one line per file higher
(+335 over this table).

⚠️ The numbers before this run said **1,564** hand-written lines in 78 files against **21,734**
generated in 313, and every one of them was a day stale in a repository where the generators change
weekly. A measured number is only measured on the day it names: this table is what the
command above printed, not what it printed once.

| | Files | Lines |
|---|---:|---:|
| Written — `src/Invoicing.Registry` | 32 | 492 |
| Written — `src/Invoicing.Billing` | 53 | 1,105 |
| Written — `src/Invoicing.Host` | 3 | 98 |
| **Generated by Pragmatic — Registry** | 123 | 6,817 |
| **Generated by Pragmatic — Billing** | 178 | 10,289 |
| **Generated by Pragmatic — Host** | 25 | 4,543 |
| Written — `tests/Invoicing.IntegrationTests` | 23 | 2,487 |
| Generated in the test project | 9 | 1,817 |

Eight of those nine files are the Testing generator's — five contract-test classes, two typed `Api`
clients and the coverage report; the ninth is ASP.NET's own OpenAPI XML-comment helper, which is not a
contract test.

**1,695 lines of application** in 88 files, against **21,649 generated by Pragmatic** in 326 — about
**thirteen** times as much, and the host's three files are 98 lines of composition. Some of the
generated total is data rather than logic: the modules' manifests and the host's OpenAPI document are
in it. Three more files are other generators' (89 + 485 lines, ASP.NET's OpenAPI helpers) and are not
in that 21,649.

Behind those lines: **6 entities** (`Organization`, `Customer`, `Invoice`, `InvoiceLine`,
`InvoiceNumberSeries`, `Payment`), **24 operations** — 8 mutations, 8 actions and 8 queries — and one
endpoint class of its own (the PDF download), behind **22 routes**. Three of the operations serve no
route: `WriteInvoiceLineMutation` is the invoice's nested line, and two queries are `[Published]`
contracts that Billing calls in process and nobody maps. So 7 + 8 + 6 + 1 is the 22, and the test that
walks the access table asserts that those 22 are exactly the routes the application maps, each
declaring the permission it needs — 19 of them belong to a company and three to none, which is the
same 22 as in *Run it*.

`OutstandingByCustomerQuery` answers with `OutstandingByCustomerLine`, a `[QueryView<Invoice>]` that
aggregates in SQL: the view is the shape of the answer, not a ninth query.

To read the generated code, build once and open `src/Invoicing.Billing/obj/Debug/net10.0/generated/`
(`EmitCompilerGeneratedFiles` is on for this example so the files exist to be read). Good places to
start:

- `Invoicing.Billing.Invoices.Actions.IssueInvoiceAction.Invoker.g.cs` — the pipeline an action runs in
- `Invoicing.Billing.Dtos.InvoiceListItemDto.Mapping.g.cs` — `FromEntity` and the EF projection beside it
- `Invoicing.Billing.Entities.Invoice.StateMachine.g.cs` — the transitions, as declared

## What it is not

- **No credit note** (`nota di credito`), which is an accountant's real answer to a wrong invoice; this
  example voids and stops there.
- **No electronic invoicing** — no `FatturaPA`, no SDI, no XML, no digital signature.
- **No payment reconciliation** — no bank import, no matching, no refunds, no partial payment plans,
  and a payment in a currency other than the invoice's is refused rather than converted.
- **No privacy chapter** — classification, export and erasure of personal data are Time off's
  (`examples/time-off`); Invoicing has the audit trail and nothing more.
- **No database per tenant** — one schema, one filter, which is the level-2 shape. A database per
  tenant is level 3.
- **No messaging** — no outbox, no broker, no saga. The reminder is a job, not an event.
- **No second host, no client**, and no deployment: the example stops at `dotnet run`.

## Where the capabilities it uses are documented

[Actions](../../Pragmatic.Actions/README.md) ·
[Endpoints](../../Pragmatic.Endpoints/README.md) ·
[Persistence](../../Pragmatic.Persistence/README.md) ·
[Mapping](../../Pragmatic.Mapping/README.md) ·
[Validation](../../Pragmatic.Validation/README.md) ·
[MultiTenancy](../../Pragmatic.MultiTenancy/README.md) ·
[Identity](../../Pragmatic.Identity/README.md) ·
[Authorization](../../Pragmatic.Authorization/README.md) ·
[Internationalization](../../Pragmatic.Internationalization/README.md) ·
[Documents](../../Pragmatic.Documents/README.md) ·
[Storage](../../Pragmatic.Storage/README.md) ·
[Jobs](../../Pragmatic.Jobs/README.md) ·
[Email](../../Pragmatic.Email/README.md) ·
[Audit](../../Pragmatic.Audit/README.md) ·
[Temporal](../../Pragmatic.Temporal/README.md) ·
[Testing](../../Pragmatic.Testing/README.md) ·
[Migrations](../../Pragmatic.Migrations/README.md)

## Known limits

- **No generated isolation contract for the invoice.** Its create carries its lines, the body
  synthesiser cannot fill a nested collection, and the generator then emits neither the create nor the
  isolation test for it. The invoice's isolation is covered by hand-written tests instead.
