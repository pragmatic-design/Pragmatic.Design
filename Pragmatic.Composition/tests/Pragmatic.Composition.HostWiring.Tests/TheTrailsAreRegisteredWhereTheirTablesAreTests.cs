// Pragmatic.Composition.HostWiring.Tests - The audit trail and the subject registry, on their database

using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     A host whose module has an <c>[Audited]</c> entity registers <c>AuditDbContext</c> and the trail on
///     that entity's database; one with a <c>[DataSubject]</c> registers <c>PrivacyDbContext</c> and the
///     registry. The application wrote both by hand before, naming what the host already declares.
/// </summary>
/// <remarks>
///     Compiled for real, module and host: a registration that reads right and does not compile is the
///     failure a text assertion cannot see.
/// </remarks>
public sealed class TheTrailsAreRegisteredWhereTheirTablesAreTests
{
    private const string AuditedEntity = """
        using Pragmatic.Persistence.Entity;

        namespace TimeOff.Leave.Entities;

        [Entity]
        [Audited]
        public partial class LeaveRequest : IEntity
        {
            public string Reason { get; private set; } = "";
        }
        """;

    private const string DataSubject = """
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Privacy;

        namespace TimeOff.Leave.Entities;

        [Entity]
        [DataSubject(nameof(Number))]
        public partial class Employee : IEntity
        {
            public string Number { get; private set; } = "";

            [PersonalData(DataCategory.Contact, Erasure = ErasureStrategy.Null)]
            public string Email { get; private set; } = "";
        }
        """;

    private const string Host = """
        using System.Threading.Tasks;
        using Pragmatic.Composition.Attributes;
        using Pragmatic.Composition.Database;
        using Pragmatic.Composition.Enums;
        using TimeOff.Leave;

        namespace TimeOff.Host;

        [PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:App")]
        public sealed class AppDatabase : PragmaticDatabase;

        [Module]
        [Include<LeaveModule, AppDatabase>]
        public sealed class TimeOffHostModule;

        internal static class Program
        {
            private static Task Main(string[] args) => Task.CompletedTask;
        }
        """;

    private const string AuditContext =
        "services.AddDbContext<global::Pragmatic.Audit.EFCore.AuditDbContext>(options => options.UseNpgsql(configuration[\"ConnectionStrings:App\"]));";

    private const string AuditTrail = "global::Pragmatic.Audit.EFCore.AuditEfCoreExtensions.AddAuditTrail(services);";

    private const string PrivacyContext = "services.AddDbContext<global::Pragmatic.Privacy.EFCore.PrivacyDbContext>(";

    private const string SubjectRegistry = "global::Pragmatic.Privacy.EFCore.PrivacyEfCoreExtensions.AddSubjectRegistry(services);";

    [Fact]
    public void AnAuditedEntity_RegistersTheTrailOnItsDatabase()
    {
        var lines = DatabaseRegistrationOf(AuditedEntity);

        lines.Should().Contain(AuditContext,
            "the trail's tables are created in the database of the [Audited] entity — and without the retry, "
            + "because the trail enlists in a transaction it did not begin");
        lines.Should().Contain(AuditTrail);
    }

    /// <summary>The control: no <c>[Audited]</c> entity, no tables, nothing to register.</summary>
    [Fact]
    public void WithoutAnAuditedEntity_TheTrailIsNotRegistered()
        => string.Join("\n", DatabaseRegistrationOf(AModuleWithoutEntitiesOnADatabaseTests.OneEntity))
            .Should().NotContain("AuditDbContext");

    [Fact]
    public void ADataSubject_RegistersTheRegistryOnItsDatabase()
    {
        var lines = DatabaseRegistrationOf(DataSubject);

        lines.Should().Contain(line => line.StartsWith(PrivacyContext, StringComparison.Ordinal),
            "the registry's tables are created in the database of the [DataSubject]");
        lines.Should().Contain(SubjectRegistry);
    }

    /// <summary>The control for the registry.</summary>
    [Fact]
    public void WithoutADataSubject_TheRegistryIsNotRegistered()
        => string.Join("\n", DatabaseRegistrationOf(AuditedEntity)).Should().NotContain("PrivacyDbContext");

    /// <summary>
    ///     A context the application registered itself wins: the check is emitted before each registration,
    ///     and it runs after <c>Program.cs</c>.
    /// </summary>
    [Fact]
    public void TheRegistrationStepsAsideForTheApplicationsOwnContext()
    {
        var lines = DatabaseRegistrationOf(AuditedEntity);
        var guard = lines.ToList().FindIndex(line => line.Contains("d.ServiceType == typeof(global::Pragmatic.Audit.EFCore.AuditDbContext)"));

        guard.Should().BeGreaterThan(-1, "the registration is conditional");
        lines.ToList().IndexOf(AuditContext).Should().BeGreaterThan(guard);
    }

    /// <summary>The lines of the generated file that registers the databases, after compiling the host.</summary>
    private static IReadOnlyList<string> DatabaseRegistrationOf(string entity)
    {
        var (errors, generated) = ModuleAndHost.Generate(
            "TimeOff.Leave",
            [AModuleWithoutEntitiesOnADatabaseTests.ModuleWithoutEntities, entity],
            "TimeOff.Host",
            Host);

        errors.Should().BeEmpty("the host has to compile, or the registration is text nobody runs");

        var file = generated.Values.FirstOrDefault(text => text.Contains("RegisterAllDatabases(this IServiceCollection", StringComparison.Ordinal));
        file.Should().NotBeNull("the host includes a module with an entity on a database");

        return [.. file!.Split('\n').Select(l => l.TrimEnd('\r').Trim()).Where(l => l.Length > 0)];
    }
}
