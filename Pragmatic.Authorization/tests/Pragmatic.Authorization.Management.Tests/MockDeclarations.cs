using Pragmatic.Testing.Assertions;
using Pragmatic;
using Pragmatic.Authorization;
using Pragmatic.Authorization.Management;
using Pragmatic.Authorization.Management.Tests;
using Pragmatic.Authorization.Management.Tests.Unit;
using Pragmatic.Identity;
using Pragmatic.Result.Http;
using Pragmatic.Testing.Mocking;

// Every type this test assembly mocks. The generator turns each into a {Type}Mock class,
// so the declarations double as the inventory: what this suite stands in for, in one place.
[assembly: GenerateMock<ICurrentUser>]
