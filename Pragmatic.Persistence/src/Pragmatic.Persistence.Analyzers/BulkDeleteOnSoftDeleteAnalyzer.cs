using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Pragmatic.Persistence.Analyzers;

/// <summary>
///     Flags a bulk delete of a soft-delete entity (PRAG0687).
/// </summary>
/// <remarks>
///     <para>
///         Every other delete path converges on <c>SaveChanges</c>, where the soft-delete interceptor
///         turns the deletion into the flag — a repository call, a mutation, a child severed from its
///         parent's collection, a hand-written <c>Remove</c>. <c>ExecuteDelete</c> reaches none of it:
///         EF translates it to a <c>DELETE</c> statement and the change tracker never sees the rows.
///     </para>
///     <para>
///         So this is the one remaining way to permanently remove a row whose entity declares it is
///         recoverable, and compile time is the only place it can be caught. Legitimate when erasure
///         is the point, which <c>SoftDeleteScope.Suspend()</c> is there to say.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BulkDeleteOnSoftDeleteAnalyzer : DiagnosticAnalyzer
{
    private const string SoftDeleteAttributeMetadataName = "Pragmatic.Persistence.Entity.SoftDeleteAttribute";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.BulkDeleteOnSoftDeleteEntity);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(static start =>
        {
            var softDelete = start.Compilation.GetTypeByMetadataName(SoftDeleteAttributeMetadataName);
            if (softDelete is null)
                return;

            start.RegisterOperationAction(ctx => AnalyzeInvocation(ctx, softDelete), OperationKind.Invocation);
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, INamedTypeSymbol softDelete)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (method.Name is not ("ExecuteDelete" or "ExecuteDeleteAsync"))
            return;

        // The entity is the queryable's element: ExecuteDelete is an extension on IQueryable<TEntity>,
        // so it is the receiver's type argument rather than anything on the method itself.
        var receiver = invocation.Instance?.Type
                       ?? (invocation.Arguments.Length > 0 ? invocation.Arguments[0].Value.Type : null);

        if (receiver is not INamedTypeSymbol { TypeArguments.Length: 1 } queryable)
            return;

        if (queryable.TypeArguments[0] is not INamedTypeSymbol entity)
            return;

        var isSoftDelete = entity.GetAttributes()
            .Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, softDelete));

        if (!isSoftDelete)
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.BulkDeleteOnSoftDeleteEntity,
            invocation.Syntax.GetLocation(),
            entity.Name));
    }
}
