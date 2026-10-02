using Pragmatic.Testing.Assertions;
using Microsoft.AspNetCore.Http;
using Pragmatic;
using Pragmatic.Composition;
using Pragmatic.Composition.Hosting;
using Pragmatic.Composition.Tests;
using Pragmatic.Composition.Tests.Hosting;
using Pragmatic.Maintenance;
using Pragmatic.Testing.Mocking;

// Every type this test assembly mocks. The generator turns each into a {Type}Mock class,
// so the declarations double as the inventory: what this suite stands in for, in one place.
[assembly: GenerateMock<IMaintenanceMode>]
