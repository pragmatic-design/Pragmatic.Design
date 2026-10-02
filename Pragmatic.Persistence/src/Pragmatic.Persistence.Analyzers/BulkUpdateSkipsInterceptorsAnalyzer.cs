using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Pragmatic.Persistence.Analyzers;

/// <summary>
///     A set-based update on an entity whose declared behaviour depends on SaveChanges.
/// </summary>
/// <remarks>
///     <para>
///         The twin of <c>PRAG0687</c>, and the same shape of trap on many more rows.
///         <c>ExecuteUpdate</c> is translated straight to SQL and never enters the change tracker, so
///         nothing that hangs off SaveChanges happens: <c>[Auditable]</c> leaves the audit columns at
///         their old values, <c>[ConcurrencyAware]</c> leaves the token untouched — a client holding a
///         stale copy will save over the change believing it fresh — and raised events are not raised.
///     </para>
///     <para>
///         The value moves, <c>UpdatedAt</c> does not, and nothing else warns. The query filters do
///         survive, because the rows are selected through a queryable
///         that carries them; it is only the write half that is skipped.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BulkUpdateSkipsInterceptorsAnalyzer : DiagnosticAnalyzer
{
    private const string AuditableAttribute = "Pragmatic.Persistence.Entity.AuditableAttribute";
    private const string ConcurrencyAwareAttribute = "Pragmatic.Persistence.Entity.ConcurrencyAwareAttribute";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.BulkUpdateSkipsInterceptors);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(static start =>
        {
            var auditable = start.Compilation.GetTypeByMetadataName(AuditableAttribute);
            var concurrency = start.Compilation.GetTypeByMetadataName(ConcurrencyAwareAttribute);
            if (auditable is null && concurrency is null)
                return;

            start.RegisterOperationAction(
                ctx => AnalyzeInvocation(ctx, auditable, concurrency),
                OperationKind.Invocation);
        });
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        INamedTypeSymbol? auditable,
        INamedTypeSymbol? concurrency)
    {
        var invocation = (IInvocationOperation)context.Operation;

        if (invocation.TargetMethod.Name is not ("ExecuteUpdate" or "ExecuteUpdateAsync"))
            return;

        // Same shape as the delete analyzer: the entity is the queryable's type argument, because
        // ExecuteUpdate is an extension on IQueryable<TEntity>.
        var receiver = invocation.Instance?.Type
                       ?? (invocation.Arguments.Length > 0 ? invocation.Arguments[0].Value.Type : null);

        if (receiver is not INamedTypeSymbol { TypeArguments.Length: 1 } queryable)
            return;

        if (queryable.TypeArguments[0] is not INamedTypeSymbol entity)
            return;

        var attributes = entity.GetAttributes();
        var skipped = new List<string>();

        if (auditable is not null
            && attributes.Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, auditable)))
            skipped.Add("[Auditable]");

        if (concurrency is not null
            && attributes.Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, concurrency)))
            skipped.Add("[ConcurrencyAware]");

        if (skipped.Count == 0)
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.BulkUpdateSkipsInterceptors,
            invocation.Syntax.GetLocation(),
            entity.Name,
            string.Join(" and ", skipped)));
    }
}
