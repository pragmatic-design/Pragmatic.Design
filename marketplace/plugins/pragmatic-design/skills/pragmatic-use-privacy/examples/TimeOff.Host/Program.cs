using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Authorization;
using Pragmatic.Composition.Hosting;
using Pragmatic.Cryptography;
using Pragmatic.Cryptography.EFCore;
using Pragmatic.Endpoints.OpenApi;
using Pragmatic.Identity.Auditing;
using Pragmatic.Identity.Local.Jwt;
using Pragmatic.Identity.Local.Services;
using Pragmatic.Internationalization;
using Pragmatic.Migrations.Extensions;
using Pragmatic.Privacy;
using Pragmatic.Result.EntityFrameworkCore.PostgreSQL;
using TimeOff.Host.Identity;
using TimeOff.Host.Privacy;
using Pragmatic.Temporal;
using TimeOff.Leave.Entities;
using TimeOff.Leave.Generated;
using TimeOff.Leave.Infrastructure.Authorization;
using TimeOff.Leave.Infrastructure.Calendar;
using TimeOff.Leave.Infrastructure.Identity;
using TimeOff.Leave.Infrastructure.Internationalization;
using TimeOff.Leave.Infrastructure.Privacy;

await PragmaticApp.RunAsync(args, app =>
{
    // The languages the module speaks, its messages, and whose language a request is answered in: the
    // one it names, else the one the employee chose, else English.
    app.UseI18N(i18n =>
    {
        i18n.DefaultCulture(Languages.English);
        i18n.Support([.. Languages.Supported]);
        // Beside the application, where the module's build copies them: a relative path would be read
        // from wherever the process happened to be started.
        i18n.AddJsonTranslations(Path.Combine(AppContext.BaseDirectory, "translations"));
        i18n.LocalizeProblemDetails();
    });
    // The employee's own choice, below the request's and above the default: the provider the generator
    // writes for Employee.PreferredCulture.
    app.UseUserCulture();

    app.UsePragmaticMigrations();

    // The API contract, as the generator wrote it at compile time — the errors each operation declares and
    // the permission each one requires — in every environment: the generated host publishes it in
    // Development on its own, with Scalar over it.
    app.UseApiDocumentation();

    // The audit trail and the subject registry live in the application's database, beside the [Audited]
    // entities and the [DataSubject] whose migration creates their tables: the generated host registers
    // both there. The registry maps each employee's reference — the one the tokens carry, so the one every
    // row and every audit entry records — to who they are, until the employee is erased. Its two keys come
    // from configuration, never from that database.
    var identityKey = ConfiguredKey.Read(app.Configuration, "Privacy:IdentityKey");
    var lookupKey = ConfiguredKey.Read(app.Configuration, "Privacy:LookupKey");
    app.Services.AddSingleton<ISecretEncryptor>(_ =>
        new AesGcmSecretEncryptor(new EncryptionKeyRing(EncryptionKey.FromMaterial(identityKey))));
    app.Services.AddSingleton<ISubjectLookupKeyProvider>(new ConfiguredLookupKey(lookupKey));

    // ⚠️ The per-subject keys that protect LeaveRequest.Reason are NOT registered here, and their
    // absence is the point: the generated host puts CryptographyDbContext on the database that holds
    // the subjects — the same one, by the same rule as the registry above — because erasing a
    // crypto-shredded column is destroying its key, and a key in another store would make that a
    // distributed transaction. What stays the application's is the master key ring the wrapped keys
    // are wrapped by, which is the ISecretEncryptor two lines up: a key kept beside what it opens
    // protects nothing.

    // Erasure: the generated steps clear what the entities classify; closing the account is the one
    // step the classification cannot reach.
    app.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IErasureStep, CloseTheAccount>());

    // A failed save is classified by its SQLSTATE and not by the words the server chose. Without this
    // the registry falls back to the heuristic parser, which reads the message text — and a PostgreSQL
    // configured in another language answers in that language.
    app.Services.AddPostgreSqlResultErrorHandling();

    // Security events on the trail: a failed sign-in and a lockout are recorded like any other fact
    // about this application. It is the host's decision and not the module's — the module never asked
    // to be audited, and the handlers listen to Identity.Local's events wherever they are raised.
    // ⚠️ The generic overload, and it matters: without a locator of the application's
    // the bridge looks the subject up under a fixed ("User", <e-mail>), this application registers its
    // subjects as ("Employee", <EmployeeNumber>), and the two can never match — so every security entry
    // is written with no subject and per-subject correlation finds nothing without saying so.
    app.Services.AddIdentitySecurityAuditing<TheEmployeeASignInWasAbout>();

    // Nothing registers AuditPatternDetector, and nothing should: it is a pure function of the trail
    // and a window, and ScanForSecurityIncidentsAction builds one per call. A lifetime on a type that
    // remembers nothing invites a reader to assume it remembers something.

    // The half of the register only the company can declare: who it is, and why it holds what it holds.
    app.Services.Configure<ProcessingRegisterOptions>(register =>
    {
        register.ControllerName = "Time off";
        register.ControllerContact = "privacy@time-off.example";
        register.Purposes[typeof(Employee).FullName!] =
            "Employment: who works here, the account they sign in with, and the language they are addressed in";
        register.Purposes[typeof(LeaveRequest).FullName!] =
            "Leave management: requests, their reasons, and the decisions taken on them";
    });

    // Working days are counted on the Italian calendar; the company's own closures come on top.
    app.UseTemporal(temporal => temporal.UseHolidayProvider<ItalianPublicHolidays>());

    // Authentication: tokens this application signs for its employees' local accounts, configured from the
    // Jwt section (Key, Issuer, Audience). The key comes from user secrets or the environment, never a
    // committed file outside Development.
    app.UseJwtAuthentication();
    // Signing in is the identity package's (SignInUser, exposed by the module); what the token says about
    // the employee is Time off's, next to the subject registry it reads the employee's reference from.
    app.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IUserClaimsContributor, EmployeeClaims>());
    app.Services.AddHostedService<FirstAdministrator>();

    // Invitations and password resets go out of band. Time off sends no email of its own: production
    // registers an email-backed notifier here; Development writes them to the log.
    if (app.Environment.IsDevelopment())
        app.Services.AddSingleton<IPasswordResetNotifier, DevelopmentMailbox>();

    // Authorization: the token carries the role, and each role's permissions are listed in its class.
    app.UseAuthorization(authz =>
    {
        authz.MapRole<EmployeeRole>();
        authz.MapRole<ManagerRole>();
        authz.MapRole<HrAdministratorRole>();
    });
}).ConfigureAwait(false);
