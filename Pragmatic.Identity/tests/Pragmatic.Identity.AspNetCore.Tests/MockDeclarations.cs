using Pragmatic.Testing.Assertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pragmatic;
using Pragmatic.Authorization;
using Pragmatic.Identity;
using Pragmatic.Identity.AspNetCore;
using Pragmatic.Identity.AspNetCore.Tests;
using Pragmatic.Identity.AspNetCore.Tests.Unit;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Pragmatic.Testing.Mocking;

// Every type this test assembly mocks. The generator turns each into a {Type}Mock class,
// so the declarations double as the inventory: what this suite stands in for, in one place.
[assembly: GenerateMock<IHttpContextAccessor>]
[assembly: GenerateMock<IOptionsMonitor<AuthenticationSchemeOptions>>]
[assembly: GenerateMock<IServiceProvider>]
[assembly: GenerateMock<IUserAuthorization>]
[assembly: GenerateMock<Microsoft.Extensions.Hosting.IHostEnvironment>]
