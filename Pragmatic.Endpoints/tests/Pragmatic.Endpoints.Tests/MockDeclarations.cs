using Pragmatic.Testing.Assertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic;
using Pragmatic.Caching;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.AspNetCore;
using Pragmatic.Endpoints.Idempotency;
using Pragmatic.Endpoints.Tests;
using Pragmatic.Endpoints.Tests.Unit;
using System.Threading.RateLimiting;
using Xunit;
using Pragmatic.Testing.Mocking;

// Every type this test assembly mocks. The generator turns each into a {Type}Mock class,
// so the declarations double as the inventory: what this suite stands in for, in one place.
[assembly: GenerateMock<ICacheStack>]

// The two sub-objects of ICurrentUser. The idempotency key needs the caller's id and nothing else,
// So these stand in for the parts the test has no opinion about.
[assembly: GenerateMock<global::Pragmatic.Authorization.IUserAuthorization>]
[assembly: GenerateMock<global::Pragmatic.Identity.IAuthenticationContext>]
