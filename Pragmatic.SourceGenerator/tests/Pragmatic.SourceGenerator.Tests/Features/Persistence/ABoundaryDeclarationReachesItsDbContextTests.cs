using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     Each boundary <c>[Enable…]</c> attribute, declared in a module's source, maps its
///     package's table into the <c>DbContext</c> the host generates, puts it in the migration schema, and
///     registers what writes it.
/// </summary>
/// <remarks>
///     <para>
///         The six wiring suites (<c>EventOutboxWiringTests</c>, <c>BatchProgressWiringTests</c>, …)
///         start from a <c>BoundaryDbContextModel</c> built by hand. They prove that the template maps
///         the table when the flag is set. They cannot prove that the attribute sets the flag. Three of
///         them also call their reader, and that stops at the reader too. A value that is read and then
///         dropped on its way to the model leaves every one of those cases green, while the
///         application's table is never created. It has happened: the
///         <c>[StoresNotifications]</c> flag reached one of the four sites it had to reach, and the first
///         run answered <c>42P01</c>.
///     </para>
///     <para>
///         So this starts where an application starts: the attribute on a boundary in a module
///         compilation, then a host that references it. The host's <c>.EFCore</c> packages are stubs,
///         because the generator only asks whether their marker type exists
///         (<c>FeatureDetector</c>). The attributes are the real types, so a reader whose name or
///         namespace drifts from the type it claims to read goes red here.
///     </para>
/// </remarks>
public class ABoundaryDeclarationReachesItsDbContextTests
{
    /// <summary>
    ///     The attribute, the line it contributes to the generated <c>OnModelCreating</c>, and the table
    ///     it adds to the migration schema.
    /// </summary>
    public static TheoryData<string, string, string> Declarations => new()
    {
        { "global::Pragmatic.Events.Attributes.EnableEventOutbox",
            "global::Pragmatic.Events.EFCore.Outbox.EventOutboxEntryConfiguration()",
            "__EventOutbox" },
        { "global::Pragmatic.Messaging.Attributes.EnableSagaPersistence",
            "global::Pragmatic.Messaging.Saga.SagaEntityTypeConfiguration()",
            "__SagaInstances" },
        { "global::Pragmatic.Messaging.Attributes.EnableOutbox",
            "global::Pragmatic.Messaging.EFCore.Outbox.MessagingOutboxExtensions.AddMessagingOutbox(modelBuilder)",
            "__OutboxMessages" },
        { "global::Pragmatic.Messaging.Attributes.EnableBatchProgress",
            "global::Pragmatic.Messaging.Batch.BatchProgressEntityTypeConfiguration()",
            "__BatchProgress" },
        { "global::Pragmatic.Jobs.Attributes.EnableJobPersistence",
            "global::Pragmatic.Jobs.EFCore.Entities.JobEntityTypeConfiguration()",
            "__Jobs" },
        { "global::Pragmatic.Notifications.Attributes.StoresNotifications",
            "global::Pragmatic.Notifications.EFCore.NotificationDbContext.ApplyNotificationConfigurations(modelBuilder)",
            "__Notifications" },
    };

    /// <summary>
    ///     The declaration maps the table into the boundary's context and puts it in the migration
    ///     schema, which is what creates it.
    /// </summary>
    /// <remarks>
    ///     Two sites, each with its own copy of the set. A context that maps a table the schema never
    ///     mirrors compiles, starts, and fails on the first write. The schema is the site once
    ///     missed.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Declarations))]
    public void TheDeclaration_MapsItsTableAndCreatesIt(string attribute, string mapping, string table)
    {
        var (context, schema, _) = Generate(attributeLine: $"[{attribute}]");

        context.Should().Contain(mapping,
            $"{attribute} is declared on the boundary, so its table belongs in the boundary's context");
        schema.Should().Contain($"\"{table}\"",
            $"{attribute} is declared on the boundary, so the migration has to create {table}");
    }

    /// <summary>
    ///     The control: the same module and host, with no declaration, map none of it and create none
    ///     of it.
    /// </summary>
    /// <remarks>
    ///     Without it, "the table is mapped" is satisfied by mapping it everywhere. The host references
    ///     every package in both cases, so the attribute is the only thing that differs.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Declarations))]
    public void WithoutTheDeclaration_TheTableIsNeitherMappedNorCreated(string attribute, string mapping, string table)
    {
        var (context, schema, _) = Generate(attributeLine: "");

        context.Should().NotContain(mapping,
            $"no boundary declares {attribute}, so no context should map its table");
        schema.Should().NotContain($"\"{table}\"",
            $"no boundary declares {attribute}, so no migration should create {table}");
    }

    /// <summary>
    ///     The attribute, and the line it contributes to the generated DbContext registration: the
    ///     interceptor or store that writes the table the two sites above create.
    /// </summary>
    /// <remarks>
    ///     Five, not six. <c>[StoresNotifications]</c> contributes nothing here: the application picks
    ///     the store itself with <c>UseEfCoreStore()</c>, and the boundary only has to own the table.
    /// </remarks>
    public static TheoryData<string, string> Registrations => new()
    {
        { "global::Pragmatic.Events.Attributes.EnableEventOutbox",
            "global::Pragmatic.Events.EFCore.Outbox.EventOutboxExtensions.AddEventOutbox<SalesDbContext>(services);" },
        { "global::Pragmatic.Messaging.Attributes.EnableSagaPersistence",
            "new global::Pragmatic.Messaging.Configuration.SagaPersistenceMarker(typeof(global::Sales.Entities.SalesDbContext))" },
        { "global::Pragmatic.Messaging.Attributes.EnableOutbox",
            "global::Pragmatic.Messaging.EFCore.Outbox.MessagingOutboxExtensions.AddMessagingOutbox<SalesDbContext>(services, \"Sales\");" },
        { "global::Pragmatic.Messaging.Attributes.EnableBatchProgress",
            "global::Pragmatic.Messaging.Batch.BatchProgressExtensions.AddBatchProgress<SalesDbContext>(services);" },
        { "global::Pragmatic.Jobs.Attributes.EnableJobPersistence",
            "services.AddScoped<global::Microsoft.EntityFrameworkCore.DbContext>(sp => sp.GetRequiredService<SalesDbContext>());" },
    };

    /// <summary>
    ///     The declaration wires what writes its table: the registration is the third site the set
    ///     reaches, with its own copy of it.
    /// </summary>
    /// <remarks>
    ///     A table that exists and that nothing writes looks like a working outbox until the first
    ///     message is missing.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Registrations))]
    public void TheDeclaration_RegistersWhatWritesItsTable(string attribute, string registration)
    {
        var registrations = Generate(attributeLine: $"[{attribute}]").Registration;

        registrations.Should().Contain(registration,
            $"{attribute} is declared on the boundary, so the host has to wire what writes its table");
    }

    /// <summary>The control: with no declaration, none of it is registered.</summary>
    [Theory]
    [MemberData(nameof(Registrations))]
    public void WithoutTheDeclaration_NothingWritesTheTable(string attribute, string registration)
    {
        var registrations = Generate(attributeLine: "").Registration;

        registrations.Should().NotContain(registration,
            $"no boundary declares {attribute}, so the host should wire nothing for it");
    }

    private static (string Context, string Schema, string Registration) Generate(string attributeLine)
    {
        var module = GeneratorTestHelper.RunGeneratorAsReference<PragmaticSourceGenerator>(
            "Sales.Module", ModuleSource(attributeLine), References);

        var result = GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(
            Host, [..References, module]);

        var context = GeneratorTestHelper.GetGeneratedSource(result, "DbContext.Sales");
        var schema = GeneratorTestHelper.GetGeneratedSource(result, "Persistence.SchemaMetadata");
        var registration = GeneratorTestHelper.GetGeneratedSource(result, "Persistence.DbContextRegistration");

        context.Should().NotBeNull(
            "the host references a module with an entity on SalesBoundary, so its context is generated "
            + "— and if this is null the assertions are about nothing");
        schema.Should().NotBeNull(
            "the host references Pragmatic.Migrations, so its schema is generated — and if this is null "
            + "the assertions are about nothing");
        registration.Should().NotBeNull(
            "the host has a boundary context to register — and if this is null the assertions are about "
            + "nothing");

        return (context!, schema!, registration!);
    }

    private static string ModuleSource(string attributeLine) => $$"""
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace Sales;

        [Boundary]
        {{attributeLine}}
        public partial class SalesBoundary;

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class Order : IEntity
        {
            public string Number { get; private set; } = "";
        }
        """;

    /// <summary>
    ///     A host that references every package whose table a boundary can ask for.
    /// </summary>
    /// <remarks>
    ///     The generator gates each set on the package being present (an
    ///     <c>[EnableJobPersistence]</c> without <c>Pragmatic.Jobs.EFCore</c> maps nothing, PRAG2508), and
    ///     it asks by the marker type alone. These are those markers, plus the one that makes the host
    ///     generate a migration schema.
    /// </remarks>
    private const string Host = """
        namespace Pragmatic.Migrations.Schema { public sealed class SchemaVersion { } }
        namespace Pragmatic.Events.EFCore { public sealed class LifecycleEventsInterceptor { } }
        namespace Pragmatic.Messaging.EFCore { public sealed class EfCoreOutboxSource { } }
        namespace Pragmatic.Messaging.Batch { public interface IBatchProgressStore { } }
        namespace Pragmatic.Jobs.EFCore.Entities { public sealed class JobEntityTypeConfiguration { } }
        namespace Pragmatic.Notifications.EFCore { public sealed class NotificationDbContext { } }

        namespace AppHost
        {
            public static class Program
            {
                public static void Main() { }
            }
        }
        """;

    /// <summary>
    ///     Everything the module and the host are compiled against.
    /// </summary>
    /// <remarks>
    ///     A name that does not resolve throws. A dropped reference does not fail loudly: the attribute
    ///     stops binding and the table is silently not mapped, which the control case would read as a
    ///     pass.
    /// </remarks>
    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Actions.Attributes.BoundaryAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.MultiTenancy.ITenantEntity>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Events.Attributes.EnableEventOutboxAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Messaging.Attributes.EnableOutboxAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Jobs.Attributes.EnableJobPersistenceAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Notifications.Attributes.StoresNotificationsAttribute>(),
        .. ByName(
            "System.Text.Json",
            "System.ComponentModel.TypeConverter",
            "System.Linq.Queryable",
            "System.ComponentModel.Annotations",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.Abstractions",
            "Microsoft.EntityFrameworkCore.Relational",
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            "Microsoft.Extensions.Logging.Abstractions",
            "Pragmatic.Result",
            "Pragmatic.Ensure",
            "Pragmatic.Specification",
            "Pragmatic.Mapping",
            "Pragmatic.Mapping.EFCore",
            "Pragmatic.Validation",
            "Pragmatic.Actions"),
    ];

    private static IEnumerable<MetadataReference> ByName(params string[] names)
        => names.Select(n => GeneratorTestHelper.TryGetAssemblyReference(n)
            ?? throw new InvalidOperationException(
                $"'{n}' does not resolve in the test process. Add the project reference, or the "
                + "compilation silently loses whatever needs it."));
}
