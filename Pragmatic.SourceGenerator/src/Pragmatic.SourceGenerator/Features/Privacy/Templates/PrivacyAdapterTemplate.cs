using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Privacy.Models;
using Pragmatic.SourceGenerator.Features.Privacy.Transforms;

namespace Pragmatic.SourceGenerator.Features.Privacy.Templates;

/// <summary>
///     What the two subject-driven adapters have in common: the database they read and the step that
///     turns an opaque subject reference back into the identity their rows are indexed by.
/// </summary>
/// <remarks>
///     Shared rather than written twice because the pair has to stay in step. An access request and an
///     erasure that disagree about which rows belong to a subject is the worst outcome available here:
///     the export shows the subject data the erasure will not touch.
/// </remarks>
internal abstract partial class PrivacyAdapterTemplate : CSharpTemplate
{
    /// <param name="model">The entity.</param>
    /// <param name="route">Its route to the subject.</param>
    /// <param name="match">How the subject's identity is compared.</param>
    /// <param name="engine">
    ///     True for an entity the Sql or documents engine persists: its repository and the route followed by key
    ///     (<c>PrivacyAdapterTemplate.Engine.cs</c>) instead of EF Core's <c>DbContext</c>.
    /// </param>
    protected PrivacyAdapterTemplate(PrivacyEntityModel model, SubjectRoute route, SubjectIdentifierMatch match, bool engine)
    {
        Model = model;
        Route = route;
        Match = match;
        Engine = engine;
    }

    /// <summary>The entity is an engine's: see the constructor.</summary>
    protected bool Engine { get; }

    protected PrivacyEntityModel Model { get; }

    protected SubjectRoute Route { get; }

    protected SubjectIdentifierMatch Match { get; }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Privacy";

    /// <summary>Nothing classified means nothing to collect and nothing to erase.</summary>
    protected override bool Validate() => Model.HasPersonalData;

    /// <summary>
    ///     Emits the fields and the constructor: the boundary's <c>DbContext</c> and the subject
    ///     registry; on an engine, the repositories <paramref name="writes" /> says.
    /// </summary>
    /// <remarks>
    ///     The context is asked for under the boundary type, the same key
    ///     <c>DbContextRegistrationTemplate</c> registers it with and the same one the entity's
    ///     repository uses. An entity that declares no boundary falls back to the unkeyed
    ///     <c>DbContext</c>, which is what a single-context application has.
    /// </remarks>
    protected void RenderDependencies(string typeName, bool writes)
    {
        if (Engine)
        {
            RenderEngineDependencies(typeName, writes);
            return;
        }

        AppendLine("private readonly global::Microsoft.EntityFrameworkCore.DbContext _db;");
        AppendLine("private readonly global::Pragmatic.Privacy.ISubjectRegistry _subjects;");
        AppendLine();

        var dbParameter = new MethodParameter(
            "global::Microsoft.EntityFrameworkCore.DbContext", "db")
        {
            Attribute = KeyedServiceAttribute()
        };

        XmlSummary($"Creates a new {typeName}.");
        XmlParam("db", "The database context holding this entity's rows.");
        XmlParam("subjects", "Resolves a subject reference back to the identity the rows carry.");

        Constructor(typeName, () =>
        {
            AppendLine("_db = db ?? throw new global::System.ArgumentNullException(nameof(db));");
            AppendLine("_subjects = subjects ?? throw new global::System.ArgumentNullException(nameof(subjects));");
        },
        [
            dbParameter,
            new MethodParameter("global::Pragmatic.Privacy.ISubjectRegistry", "subjects")
        ]);
    }

    /// <summary>
    ///     Emits the reference → identity step, and the early exit for a subject that is unknown or
    ///     already forgotten.
    /// </summary>
    /// <param name="nothingToDo">The expression to return when there is no identity to look up by.</param>
    /// <remarks>
    ///     Returning empty on a null identity is not a shortcut. After an erasure the registry stops
    ///     resolving the reference, and that is exactly what "there is nothing here about this person"
    ///     has to look like from the outside.
    /// </remarks>
    protected void RenderIdentityLookup(string nothingToDo)
    {
        AppendLine("var identity = await _subjects.ResolveIdentityAsync(subjectRef, ct).ConfigureAwait(false);");
        AppendLine();
        Comment("Unknown or already forgotten: the reference no longer names a person, so nothing is held.");
        AppendLine($"if (identity is null) return {nothingToDo};");

        if (Match.GuardStatement(nothingToDo) is { } guard)
        {
            AppendLine();
            Comment("Converted before the query, not inside it: a ToString() comparison either fails to " +
                    "translate or scans every row in the table.");
            AppendLine(guard);
        }

        AppendLine();
    }

    /// <summary>
    ///     Emits the LINQ predicate that selects this entity's rows for the subject.
    /// </summary>
    protected string RowsForSubject() => $"row => row.{Route.IdentifierAccess} == {Match.Operand}";

    /// <summary>The fully qualified name of a sibling type generated into the entity's namespace.</summary>
    protected string Sibling(string typeName)
        => string.IsNullOrEmpty(Model.Namespace) ? typeName : $"{Model.Namespace}.{typeName}";

    private string? KeyedServiceAttribute()
        => string.IsNullOrEmpty(Model.BoundaryTypeFullName)
            ? null
            : "global::Microsoft.Extensions.DependencyInjection.FromKeyedServices(" +
              $"typeof(global::{Model.BoundaryTypeFullName}))";
}
