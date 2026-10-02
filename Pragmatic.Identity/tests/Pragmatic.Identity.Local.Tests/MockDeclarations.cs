using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pragmatic;
using Pragmatic.Events;
using Pragmatic.Identity;
using Pragmatic.Identity.Local;
using Pragmatic.Identity.Local.Actions;
using Pragmatic.Identity.Local.Errors;
using Pragmatic.Identity.Local.Events;
using Pragmatic.Identity.Local.Services;
using Pragmatic.Identity.Local.Tests;
using Pragmatic.Identity.Local.Tests.Unit;
using Pragmatic.Result;
using Pragmatic.Temporal.Clock;
using Pragmatic.Testing.Mocking;

// Every type this test assembly mocks. The generator turns each into a {Type}Mock class,
// so the declarations double as the inventory: what this suite stands in for, in one place.
[assembly: GenerateMock<IAccessTokenIssuer>]
[assembly: GenerateMock<IAuthenticationContext>]
[assembly: GenerateMock<IClock>]
[assembly: GenerateMock<ICurrentUser>]
[assembly: GenerateMock<IDomainEventDispatcher>]
[assembly: GenerateMock<IEmailVerificationNotifier>]
[assembly: GenerateMock<ILocalIdentityStore>]
[assembly: GenerateMock<IPasswordHasher>]
[assembly: GenerateMock<IPasswordPolicy>]
[assembly: GenerateMock<IPasswordResetNotifier>]
[assembly: GenerateMock<ISecurityTokenService>]
[assembly: GenerateMock<IUserClaimsContributor>]
