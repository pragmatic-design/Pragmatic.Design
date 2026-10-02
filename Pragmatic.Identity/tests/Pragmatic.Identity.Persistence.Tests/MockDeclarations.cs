using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pragmatic;
using Pragmatic.Identity;
using Pragmatic.Identity.Persistence.Stores;
using Pragmatic.Identity.Persistence.Tests;
using Pragmatic.Identity.Persistence.Tests.Unit;
using Pragmatic.Temporal.Clock;
using Pragmatic.Testing.Mocking;

// Every type this test assembly mocks. The generator turns each into a {Type}Mock class,
// so the declarations double as the inventory: what this suite stands in for, in one place.
[assembly: GenerateMock<IClock>]
