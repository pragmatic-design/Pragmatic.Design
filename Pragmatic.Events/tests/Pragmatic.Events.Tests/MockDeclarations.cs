using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Events;
using Pragmatic.Events.Tests.Fixtures;
using Pragmatic.Testing.Mocking;

// Every type this test assembly mocks. The generator turns each into a {Type}Mock class, so the
// declarations double as the inventory: what this suite stands in for, in one place.
[assembly: GenerateMock<IDomainEventDispatcher>]
[assembly: GenerateMock<IDomainEventHandler<TestDomainEvent>>]
[assembly: GenerateMock<IServiceProvider>]
[assembly: GenerateMock<IServiceScope>]
[assembly: GenerateMock<IServiceScopeFactory>]
