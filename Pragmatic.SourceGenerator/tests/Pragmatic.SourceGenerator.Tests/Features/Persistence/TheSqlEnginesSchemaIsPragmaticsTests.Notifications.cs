using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     <c>[StoresNotifications]</c> (#528): the Sql engine's <c>__Notifications</c>, Pragmatic.Notifications.Engines' row, is EF Core's table,
///     column for column and index for index, so one database holds one table whichever engine writes it.
/// </summary>
public partial class TheSqlEnginesSchemaIsPragmaticsTests
{
    /// <summary>A boundary storing the notifications, with one entity: a database's schema exists only where an entity lives.</summary>
    private const string NotifyingBoundary = """
        global using System;
        using Pragmatic.Persistence.Entity;

        namespace Probe.Messages
        {
            [Pragmatic.Actions.Attributes.Boundary]
            [Pragmatic.Notifications.Attributes.StoresNotifications]
            public sealed partial class MessagesBoundary;

            [Entity]
            public partial class Thread : IEntity
            {
                public string Title { get; private set; } = "";
            }
        }
        """;

    /// <summary>Pragmatic.Notifications.EFCore, by the type the generator recognises it by.</summary>
    private const string NotificationsEfCore = "namespace Pragmatic.Notifications.EFCore { public sealed class NotificationDbContext { } }";

    [Theory]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    [InlineData("Sqlite")]
    public void TheNotificationsTable_IsTheSameTableOnBothBranches(string provider)
    {
        Type[] notifications = [typeof(global::Pragmatic.Actions.Attributes.BoundaryAttribute), typeof(global::Pragmatic.Notifications.Attributes.StoresNotificationsAttribute)];
        var (efOutput, _) = Run([NotifyingBoundary, EfCoreGate, Host, ProviderProbe(provider), NotificationsEfCore], notifications);
        var efCore = Describe(Read(Generated(efOutput, "_Infra.Persistence.SchemaMetadata.g.cs"), "DefaultSchema", "Current")).ToList();
        var (sqlOutput, diagnostics) = Run([NotifyingBoundary, Host, SqlBridge, DriverProbe(provider), PackageSource("INotificationFields.cs"), PackageSource("NotificationRow.cs")],
            [typeof(global::Pragmatic.Persistence.Sql.SqlSession), .. notifications]);
        diagnostics.Should().NotContain(d => d.Id == "PRAG3027", "the row has a schema on the Sql engine");
        diagnostics.Should().NotContain(d => d.Id == "PRAG2100", "the engines package is referenced");
        var engine = Describe(Read(Generated(sqlOutput, "_Infra.Persistence.SchemaMetadata.g.cs"), "DefaultSchema", "Current")).ToList();

        efCore.Should().Contain(l => l.Trim().StartsWith("index IX___Notifications_Status_CreatedAt (Status, CreatedAt)", StringComparison.Ordinal),
            "the control: EF Core's table is there, with its composite index");
        Notifications(efCore).Except(Notifications(engine)).Concat(Notifications(engine).Except(Notifications(efCore))).Should().BeEmpty();
    }

    /// <summary>The lines of <c>__Notifications</c> only: the boundary's own entity is compared by the other tests.</summary>
    private static List<string> Notifications(IEnumerable<string> schema)
        => [.. schema.Where(l => l.Contains("__Notifications", StringComparison.Ordinal))];

    /// <summary>A file of Pragmatic.Notifications.Engines, from the package's source (embedded): the row, and the fields it implements.</summary>
    private static string PackageSource(string name)
    {
        using var stream = typeof(TheSqlEnginesSchemaIsPragmaticsTests).Assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
