// Pragmatic.Composition.HostWiring.Tests - A module with no entities, included on a database

using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     A host that includes a module on a database wires that database only when an entity is behind
///     it: the schema, the migration context and the <c>Add{Boundary}DbContext</c> it names are written
///     by the persistence generator, and only for entities.
/// </summary>
/// <remarks>
///     <para>
///         The first state of every scaffolded application is a module with no entities yet. A host that
///         named <c>AppDatabaseSchema</c>, <c>AppDatabaseMigrationDbContext</c> and
///         <c>AddLeaveDbContext</c> on the strength of the <c>[Include]</c> alone would not compile there.
///     </para>
///     <para>
///         The control is the same module with one entity. Without it, "the host compiles" is also
///         satisfied by a host that stopped wiring databases altogether.
///     </para>
/// </remarks>
public sealed class AModuleWithoutEntitiesOnADatabaseTests
{
    internal const string ModuleWithoutEntities = """
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Composition.Attributes;

        namespace TimeOff.Leave;

        [Boundary]
        public partial class LeaveBoundary;

        [Module(Name = "TimeOff.Leave")]
        public sealed class LeaveModule;
        """;

    internal const string OneEntity = """
        using Pragmatic.Persistence.Entity;

        namespace TimeOff.Leave.Entities;

        [Entity]
        public partial class Employee : IEntity
        {
            public string Name { get; private set; } = "";
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

    /// <summary>What the host names when a database has entities behind it.</summary>
    private static readonly string[] DatabaseWiring =
    [
        "global::TimeOff.AppDatabaseSchema",
        "global::TimeOff.AppDatabaseMigrationDbContext",
        "services.AddLeaveDbContext("
    ];

    [Fact]
    public void WithoutEntities_TheHostCompiles()
    {
        var (errors, _) = GenerateHost(withAnEntity: false);

        errors.Should().BeEmpty("a module with no entities yet is the first state of every application");
    }

    [Fact]
    public void WithoutEntities_TheHostNamesNoneOfTheDatabaseWiring()
    {
        var (_, generated) = GenerateHost(withAnEntity: false);

        DatabaseWiring.Where(generated.Contains).Should().BeEmpty(
            "nothing generates them for a database with no entity behind it");
    }

    /// <summary>The control: one entity, and the database is wired as before.</summary>
    [Fact]
    public void WithOneEntity_TheHostStillWiresTheDatabase()
    {
        var (errors, generated) = GenerateHost(withAnEntity: true);

        errors.Should().BeEmpty("the schema, the migration context and the registration are all generated");
        DatabaseWiring.Where(name => !generated.Contains(name)).Should().BeEmpty(
            "an entity is behind the database, so its wiring is emitted");
    }

    /// <summary>
    ///     Compiling is not the bar: an application built with <c>-warnaserror</c> fails on a warning in
    ///     a file it cannot edit. With no database to migrate, an initialization block that declares its
    ///     progress stream and uses it nowhere raises CS0219.
    /// </summary>
    [Fact]
    public void WithoutEntities_TheGeneratedHostRaisesNoWarning()
        => GeneratedWarnings(withAnEntity: false).Should().BeEmpty(
            "the first state of an application has to build under -warnaserror too");

    /// <summary>The control: with an entity the same host was already warning-free.</summary>
    [Fact]
    public void WithOneEntity_TheGeneratedHostRaisesNoWarning()
        => GeneratedWarnings(withAnEntity: true).Should().BeEmpty(
            "the generated host builds under -warnaserror in every example");

    private static ImmutableArray<string> GeneratedWarnings(bool withAnEntity)
        => ModuleAndHost.GeneratedWarnings(
            "TimeOff.Leave",
            withAnEntity ? [ModuleWithoutEntities, OneEntity] : [ModuleWithoutEntities],
            "TimeOff.Host",
            Host);

    private static (ImmutableArray<string> Errors, string Generated) GenerateHost(bool withAnEntity)
    {
        var (errors, files) = ModuleAndHost.Generate(
            "TimeOff.Leave",
            withAnEntity ? [ModuleWithoutEntities, OneEntity] : [ModuleWithoutEntities],
            "TimeOff.Host",
            Host);

        return (errors, string.Join("\n", files.Values));
    }
}
