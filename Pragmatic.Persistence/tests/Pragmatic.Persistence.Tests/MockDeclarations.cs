using Pragmatic.Testing.Assertions;
using Pragmatic;
using Pragmatic.Persistence;
using Pragmatic.Persistence.Repository;
using Pragmatic.Persistence.Tests;
using Pragmatic.Persistence.Tests.Repository;
using Pragmatic.Result;
using Pragmatic.Testing.Mocking;

// Every type this test assembly mocks. The generator turns each into a {Type}Mock class,
// so the declarations double as the inventory: what this suite stands in for, in one place.
[assembly: GenerateMock<ITransaction>]
[assembly: GenerateMock<IUnitOfWork>]
