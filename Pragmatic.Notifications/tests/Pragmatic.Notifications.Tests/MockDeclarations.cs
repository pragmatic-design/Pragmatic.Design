using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pragmatic;
using Pragmatic.Email;
using Pragmatic.Notifications;
using Pragmatic.Notifications.Channels;
using Pragmatic.Notifications.Email;
using Pragmatic.Notifications.Pipeline;
using Pragmatic.Notifications.Preferences;
using Pragmatic.Notifications.Slack;
using Pragmatic.Notifications.Sms;
using Pragmatic.Notifications.Tests;
using Pragmatic.Notifications.Tests.Unit;
using Pragmatic.Notifications.Tracking;
using Pragmatic.Notifications.Webhook;
using System.Net;
using Pragmatic.Testing.Mocking;

// Every type this test assembly mocks. The generator turns each into a {Type}Mock class,
// so the declarations double as the inventory: what this suite stands in for, in one place.
[assembly: GenerateMock<IEmailSender>]
[assembly: GenerateMock<IHttpClientFactory>]
[assembly: GenerateMock<INotificationChannel>]
[assembly: GenerateMock<INotificationChannelFactory>]
[assembly: GenerateMock<INotificationPreferenceProvider>]
[assembly: GenerateMock<INotificationRouter>]
[assembly: GenerateMock<IRecipientResolver>]
