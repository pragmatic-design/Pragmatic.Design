# Examples: pragmatic-use-testing

Copied from `examples`, which compiles in the repository and is exercised by
`examples/invoicing/tests/Invoicing.IntegrationTests` and `examples/showcase/tests/Showcase.Tests`. **Do not edit here**: change the source and run
`node scripts/sync-skill-examples.mjs`. The gate refuses a copy that differs from its source.

| File | What it shows |
|---|---|
| [`invoicing/tests/Invoicing.IntegrationTests/Infrastructure/InvoicingWebFactory.cs`](invoicing/tests/Invoicing.IntegrationTests/Infrastructure/InvoicingWebFactory.cs) | The host under test is the generated host: only the connection string, the identity provider and the storage replaced, settings through `UseSetting` |
| [`invoicing/tests/Invoicing.IntegrationTests/Infrastructure/PostgresFixture.cs`](invoicing/tests/Invoicing.IntegrationTests/Infrastructure/PostgresFixture.cs) | One PostgreSQL container per run, and a fresh database for a class whose reads cover everything |
| [`invoicing/tests/Invoicing.IntegrationTests/Infrastructure/ContractAppFixture.cs`](invoicing/tests/Invoicing.IntegrationTests/Infrastructure/ContractAppFixture.cs) | The fixture of the generated contract tests: `PragmaticContractHost.Client`, `BodyFor` for the creates the generator cannot fill, `PrepareRequest` swapping the header identity for a real token |
| [`invoicing/tests/Invoicing.IntegrationTests/Infrastructure/TheContractTestsRanAtAll.cs`](invoicing/tests/Invoicing.IntegrationTests/Infrastructure/TheContractTestsRanAtAll.cs) | Pinning what the generator emitted: a suite that emits nothing looks exactly like one that passes |
| [`invoicing/tests/Invoicing.IntegrationTests/ChasingOverdueInvoices.cs`](invoicing/tests/Invoicing.IntegrationTests/ChasingOverdueInvoices.cs) | A hand-written test: `TestClock`, the mail harness, and a job driven through the unit it calls with the day as an argument |
| [`showcase/tests/Showcase.Tests/MockDeclarations.cs`](showcase/tests/Showcase.Tests/MockDeclarations.cs) | Generated mocks and comparers, declared once per test assembly with `[assembly: GenerateMock<T>]` and `[assembly: GenerateComparer<T>]` |
| [`showcase/tests/Showcase.Tests/Unit/EventHandlers/WhenBookingIsDownTheHandlerStopsTryingTests.cs`](showcase/tests/Showcase.Tests/Unit/EventHandlers/WhenBookingIsDownTheHandlerStopsTryingTests.cs) | A unit test with those mocks: a boundary interface mocked, a member made to throw, its calls counted |
