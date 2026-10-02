using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Migrations.Templates;

/// <summary>
///     Registers a typed connection factory for every database driver the compilation references.
/// </summary>
/// <remarks>
///     <c>MigrationsBuilder</c> has to know which providers it can actually open a connection with, and
///     <c>Pragmatic.Migrations</c> references no driver — so it asked at run time, with
///     <c>Type.GetType("Npgsql.NpgsqlConnection, Npgsql")</c> and then
///     <c>Activator.CreateInstance(type, connectionString)</c>. The compilation that references the
///     driver can simply write <c>new NpgsqlConnection(cs)</c>, and this is that compilation.
/// </remarks>
internal sealed class MigrationDriverRegistrationTemplate : CSharpTemplate
{
    /// <summary>Provider key ↔ driver connection type, in the order MigrationsBuilder registers them.</summary>
    /// <remarks>
    ///     The keys are the literal values of <c>MigrationConstants.Provider*</c>. The registry compares
    ///     case-insensitively, so a mismatch in casing would still work and would still be a latent
    ///     defect — matching them exactly means the lookup does not depend on that.
    /// </remarks>
    private static readonly (string Provider, string ConnectionType)[] Drivers =
    [
        ("PostgreSql", "global::Npgsql.NpgsqlConnection"),
        ("SqlServer", "global::Microsoft.Data.SqlClient.SqlConnection"),
        ("Sqlite", "global::Microsoft.Data.Sqlite.SqliteConnection")
    ];

    private readonly bool[] _available;
    private readonly string _namespace;

    public MigrationDriverRegistrationTemplate(string generatedNamespace, bool postgres, bool sqlServer, bool sqlite)
    {
        _namespace = generatedNamespace;
        _available = [postgres, sqlServer, sqlite];
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Migrations";
    protected override string? TriggerInfo => "referenced database drivers";

    protected override bool Validate() => _available.Any(a => a);

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForAssembly("Migrations", "DriverRegistration"), ToSourceText());

    public override void RenderFile()
    {
        AppendNamespace(_namespace);
        AppendLine();

        XmlSummary("Registers the connection factories for the drivers this application references.");
        Class("PragmaticMigrationDrivers", RenderBody,
            accessModifier: AccessModifier.Internal,
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderBody()
    {
        AppendLine("[global::System.Runtime.CompilerServices.ModuleInitializer]");
        AppendLine("internal static void Register()");
        Block(() =>
        {
            for (var i = 0; i < Drivers.Length; i++)
            {
                if (!_available[i])
                    continue;

                var (provider, connectionType) = Drivers[i];
                AppendLine("global::Pragmatic.Migrations.Configuration.MigrationDriverRegistry.Register(");
                IncreaseIndent();
                AppendLine($"\"{StringHelper.CSharpLiteral(provider)}\",");
                AppendLine($"connectionString => new {connectionType}(connectionString));");
                DecreaseIndent();
            }
        });
    }
}
