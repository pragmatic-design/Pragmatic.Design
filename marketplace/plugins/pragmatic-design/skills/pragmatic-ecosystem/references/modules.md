# Module Names For Consumers

Use this file to avoid stale package names and invented modules. If the agent cannot see Pragmatic source code, treat these as package names and public namespaces rather than paths to inspect.

## Foundation

| Package | Canonical source | Purpose |
|---|---|---|
| `Pragmatic.Abstractions` | `Pragmatic.Abstractions/src/Pragmatic.Abstractions/` | Shared interfaces and attributes, including Composition metadata |
| `Pragmatic.Result` | `Pragmatic.Result/src/Pragmatic.Result/` | `Result<T,E>` and `Error` types |
| `Pragmatic.Ensure` | `Pragmatic.Ensure/src/Pragmatic.Ensure/` | Guard clauses for programming errors |

## Capabilities

| Package | Canonical source | Purpose |
|---|---|---|
| `Pragmatic.Validation` | `Pragmatic.Validation/src/Pragmatic.Validation/` | Attribute validation and async validators |
| `Pragmatic.Mapping` | `Pragmatic.Mapping/src/Pragmatic.Mapping/` | DTO/entity mapping and projection |
| `Pragmatic.Specification` | `Pragmatic.Specification/src/Pragmatic.Specification/` | Reusable query predicates |
| `Pragmatic.Caching` | `Pragmatic.Caching/src/Pragmatic.Caching/` | Cache keys, tags, invalidation |
| `Pragmatic.Temporal` | `Pragmatic.Temporal/src/Pragmatic.Temporal/` | Clock, business days, timezone/date helpers |
| `Pragmatic.Internationalization` | `Pragmatic.Internationalization/src/Pragmatic.Internationalization/` | Money, currencies, formatting, translations |
| `Pragmatic.Patch` | `Pragmatic.Patch/src/Pragmatic.Patch/` | Tri-state PATCH DTOs |

## Application Integration

| Package | Canonical source | Purpose |
|---|---|---|
| `Pragmatic.Composition` | `Pragmatic.Composition/src/Pragmatic.Composition.Host/` plus `Pragmatic.Abstractions` attributes | Host bootstrap, DI, modules, startup steps |
| `Pragmatic.Actions` | `Pragmatic.Actions/src/Pragmatic.Actions/` | Domain actions, mutations, pipelines |
| `Pragmatic.Endpoints` | `Pragmatic.Endpoints/src/Pragmatic.Endpoints/` | Source-generated ASP.NET endpoints |
| `Pragmatic.Persistence` | `Pragmatic.Persistence/src/Pragmatic.Persistence/` | Entities, queries, repositories, identifiers |
| `Pragmatic.Persistence.EFCore` | `Pragmatic.Persistence/src/Pragmatic.Persistence.EFCore/` | EF Core runtime, DbContext, interceptors |
| `Pragmatic.Authorization` | `Pragmatic.Authorization/src/Pragmatic.Authorization/` | Permissions and resource authorization |
| `Pragmatic.Identity` | `Pragmatic.Identity/src/` | Current user, local/JWT identity |
| `Pragmatic.Messaging` | `Pragmatic.Messaging/src/Pragmatic.Messaging/` | Message bus, outbox, sagas |
| `Pragmatic.Jobs` | `Pragmatic.Jobs/src/Pragmatic.Jobs/` | Background and recurring jobs |
| `Pragmatic.Events` | `Pragmatic.Events/src/Pragmatic.Events/` | In-process domain events, event outbox (`.EFCore`) |
| `Pragmatic.MultiTenancy` | `Pragmatic.MultiTenancy/src/` | Tenant context and filters; `.AspNetCore` resolution; `.Persistence` DB-per-tenant |
| `Pragmatic.Migrations` | `Pragmatic.Migrations/src/Pragmatic.Migrations/` | Declarative schema migrations; `Pragmatic.Migrations.Cli` tool |
| `Pragmatic.Configuration` | `Pragmatic.Configuration/src/` | Typed options, runtime stores and backends, `.Management` admin actions |
| `Pragmatic.FeatureFlags` | `Pragmatic.FeatureFlags/src/` | Flags, targeting, rollout; `.Configuration` store |
| `Pragmatic.Resilience` | `Pragmatic.Resilience/src/Pragmatic.Resilience/` | Retry, circuit breaker, timeout, hedging, rate limit, bulkhead |
| `Pragmatic.Logging` | `Pragmatic.Logging/src/Pragmatic.Logging/` | Logging providers, enrichment, redaction presets |
| `Pragmatic.Client` | `Pragmatic.Client/src/` | Typed .NET client of a Pragmatic API; `Pragmatic.Client.Cli` for TypeScript |

## Content, files and communication

| Package | Canonical source | Purpose |
|---|---|---|
| `Pragmatic.Storage` | `Pragmatic.Storage/src/` | `IFileStorage`; `.Azure`, `.S3`, `.GoogleCloud`, `.Sftp`, `.Ftp`, `.InMemory` |
| `Pragmatic.Imaging` | `Pragmatic.Imaging/src/Pragmatic.Imaging/` | Resize, convert, thumbnails, EXIF strip, QR (native) |
| `Pragmatic.Documents.*` | `Pragmatic.Documents/src/` | `.Templating` + `.Markup` (`.pdxdoc`/`.pdxemail`), `.Pdf`, `.Docx`, `.Csv`, `.Xlsx`, `.Email` |
| `Pragmatic.Email` | `Pragmatic.Email/src/` | Sending: SMTP, DKIM, S/MIME, `.Testing` |
| `Pragmatic.Notifications` | `Pragmatic.Notifications/src/` | One pipeline over email, webhook (`.Webhook`), Slack, SMS; `.EFCore` tracking |
| `Pragmatic.Comments` / `.Tags` / `.Attachments` / `.Notes` | `Pragmatic.{Trait}/src/` | Generated child features on an entity |

## Compliance and security

| Package | Canonical source | Purpose |
|---|---|---|
| `Pragmatic.Privacy` | `Pragmatic.Privacy/src/` | Personal-data classification, erasure, access, processing register; `.EFCore` subject registry |
| `Pragmatic.Audit` | `Pragmatic.Audit/src/` | Tamper-evident trail; `.EFCore`, `.AdoNet`, `.Management` (retention) |
| `Pragmatic.Incidents` | `Pragmatic.Incidents/src/` | Security incidents and reporting deadlines; `.Audit` detection from the trail |
| `Pragmatic.Cryptography` | `Pragmatic.Cryptography/src/` | Key ring, AES-GCM, per-subject keys (`.EFCore`) |
| `Pragmatic.Redaction` | `Pragmatic.Redaction/src/Pragmatic.Redaction/` | Pattern and declared redaction for logs and the trail |
| `Pragmatic.Authorization.Management` | `Pragmatic.Authorization/src/Pragmatic.Authorization.Management/` | RBAC admin actions — see the enforcement caveat in `pragmatic-use-authorization` |
| `Pragmatic.Identity.*` | `Pragmatic.Identity/src/` | `.Local`, `.Local.Jwt`, `.Oidc`, `.Keycloak`, `.Persistence`, `.Auditing`, `.AspNetCore` |

## Several services

| Package | Canonical source | Purpose |
|---|---|---|
| `Pragmatic.Agent.Client` | `Pragmatic.Agent/src/Pragmatic.Agent.Client/` | Host side of the Agent: shared config/flags, route announcement |
| `Pragmatic.Agent.Discovery` / `Pragmatic.Discovery` | `Pragmatic.Agent/src/…`, `Pragmatic.Discovery/src/` | Topology registration and validation |
| `Pragmatic.Messaging.RabbitMQ` / `.Kafka` / `.AzureServiceBus` | `Pragmatic.Messaging/src/` | Broker transports |

The Agent daemon (`Pragmatic.Agent`) and the gateway (`Pragmatic.Gateway`) are executables built from
the repository, not packages an application references.

## Testing

| Package | Canonical source | Purpose |
|---|---|---|
| `Pragmatic.Testing` | `Pragmatic.Testing/src/Pragmatic.Testing/` | Host fixture, HTTP assertions, captured logs; generators for contract tests, mocks, comparers |

## Important Non-Modules

- There is no `Pragmatic.DependencyInjection` package. DI generation belongs to Composition.
- There is no `Pragmatic.Identifiers` package. Identifier helpers belong to `Pragmatic.Persistence.Identifiers`.
- Do not create per-module source-generator projects unless the repo already has a deliberate exception.

## Examples

- End-to-end reference app: `examples/showcase/`
- External package consumer samples: `examples/consumer-samples/`
