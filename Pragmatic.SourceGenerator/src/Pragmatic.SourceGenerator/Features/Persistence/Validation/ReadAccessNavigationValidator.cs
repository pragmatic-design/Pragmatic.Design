using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Persistence.Diagnostics;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Validation;

/// <summary>
///     Reports <c>PRAG0639</c>: a <c>[ReadAccess&lt;TChild&gt;]</c> whose child declares a navigation to
///     a type the reading boundary neither owns nor reads.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <c>[ReadAccess]</c> brings the owner's whole per-entity configuration, and that
///         configuration declares the child's navigations. A target the reader does not have goes into
///         <c>modelBuilder.Ignore&lt;T&gt;()</c> — <c>DbContextFeature.CollectCrossBoundaryTypes</c>
///         puts it there — so the declared navigation is left pointing at a type the model does not
///         know.
///     </para>
///     <para>
///         The model still builds, which is what makes it quiet: the <c>DbSet</c> is generated, the
///         configuration is applied, and the navigation is a property that exists on the CLR type.
///         What fails is the first query that names it, at run time, in an application that compiled
///         clean. Distinct from the case where the whole boundary answers 500 at first use.
///     </para>
///     <para>
///         Reported rather than repaired: pulling the missing type in would make <c>[ReadAccess]</c>
///         reach as far as the object graph does, which is exactly what is unsafe. The
///         reader is told which read is incomplete and decides whether to widen it.
///     </para>
///     <para>
///         ⚠️ Only for <b>read</b> entities. A boundary's own entity navigating to another boundary's
///         type is the ordinary cross-boundary case: the foreign key is generated, the navigation is
///         not, and that is the rule rather than a defect.
///     </para>
/// </remarks>
internal static class ReadAccessNavigationValidator
{
    /// <summary>
    ///     Reports every read entity whose navigation target the reading boundary does not have.
    /// </summary>
    public static void Validate(
        SourceProductionContext context,
        ImmutableArray<EntityMetadataModel> entities,
        EquatableDictionary<string, EquatableArray<string>> readAccessByBoundary)
    {
        if (readAccessByBoundary.IsEmpty || entities.IsDefaultOrEmpty)
            return;

        var entityByName = new Dictionary<string, EntityMetadataModel>(StringComparer.Ordinal);
        foreach (var entity in entities)
        {
            if (entity.IsValid)
                entityByName[Normalize(entity.FullTypeName)] = entity;
        }

        foreach (var declaration in readAccessByBoundary)
        {
            var boundary = Normalize(declaration.Key);

            var reads = new HashSet<string>(StringComparer.Ordinal);
            foreach (var read in declaration.Value)
                reads.Add(Normalize(read));

            // What the boundary owns is not in the declaration: it is every entity that named it.
            var owns = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entity in entities)
            {
                if (entity.IsValid && Normalize(entity.BoundaryTypeFullName ?? "") == boundary)
                    owns.Add(Normalize(entity.FullTypeName));
            }

            foreach (var read in declaration.Value)
            {
                if (!entityByName.TryGetValue(Normalize(read), out var child))
                    continue;

                foreach (var navigation in child.Navigations)
                {
                    // An owned type travels inside its parent's table and is never ignored.
                    if (navigation.IsOwned || string.IsNullOrEmpty(navigation.TargetFullTypeName))
                        continue;

                    // ⚠️ A many-to-many pointing back at the read entity is already handled: the
                    // reading context drops that navigation by name, which is what made [ReadAccess]
                    // on such an entity usable at all. Reporting it would ask the author to declare a
                    // join entity nothing will name — and with --warnaserror that is a build broken by
                    // a warning about something the framework has already dealt with. Measured on the
                    // consumer, where KnowledgeItem's self-relation produced exactly that.
                    if (navigation.NavigationType == "ManyToMany"
                        && Normalize(navigation.TargetFullTypeName!) == Normalize(child.FullTypeName))
                        continue;

                    Report(navigation.TargetFullTypeName!, navigation.TargetTypeName);

                    // ⚠️ And the join entity of a many-to-many, which is a second missing type the
                    // target alone never names. Following this diagnostic to the letter without it
                    // leaves a model EF refuses to build — "the skip navigation 'X.Ys' doesn't have a
                    // foreign key associated with it" — and then *every* request answers 500, not only
                    // the one that reads through the navigation. Measured on the Showcase while
                    // closing the cascade this diagnostic opened.
                    if (!string.IsNullOrEmpty(navigation.JoinEntityTypeName))
                        Report(navigation.JoinEntityTypeName!, SimpleName(navigation.JoinEntityTypeName!));

                    void Report(string targetFullName, string targetName)
                    {
                        var target = Normalize(targetFullName);
                        if (reads.Contains(target) || owns.Contains(target))
                            return;

                        context.ReportDiagnostic(Diagnostic.Create(
                            PersistenceDiagnostics.ReadNavigationTargetIsNotRead,
                            child.DeclarationLocation?.ToLocation() ?? Location.None,
                            child.TypeName,
                            navigation.Name,
                            targetName,
                            SimpleName(declaration.Key)));
                    }
                }
            }
        }
    }

    private static string Normalize(string typeName) =>
        typeName.StartsWith("global::", StringComparison.Ordinal) ? typeName.Substring(8) : typeName;

    private static string SimpleName(string typeName)
    {
        var normalized = Normalize(typeName);
        var lastDot = normalized.LastIndexOf('.');
        return lastDot < 0 ? normalized : normalized.Substring(lastDot + 1);
    }
}
