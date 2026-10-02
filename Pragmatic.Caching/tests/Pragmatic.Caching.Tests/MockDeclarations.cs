using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic;
using Pragmatic.Caching;
using Pragmatic.Caching.Redis;
using Pragmatic.Caching.Tests;
using Pragmatic.Caching.Tests.Unit;
using StackExchange.Redis;
using Xunit;
using Pragmatic.Testing.Mocking;

// Every type this test assembly mocks. The generator turns each into a {Type}Mock class,
// so the declarations double as the inventory: what this suite stands in for, in one place.
[assembly: GenerateMock<ICacheStack>]
[assembly: GenerateMock<IConnectionMultiplexer>]
[assembly: GenerateMock<IDatabase>]
[assembly: GenerateMock<ISubscriber>]
