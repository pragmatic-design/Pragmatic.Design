using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Pragmatic.Persistence.Analyzers;

/// <summary>
///     Flags EF Core <c>*Raw</c> SQL APIs (<c>FromSqlRaw</c>, <c>ExecuteSqlRaw</c>,
///     <c>ExecuteSqlRawAsync</c>, <c>SqlQueryRaw</c>) called with a NON-constant SQL string
///     (PRAG0684). These APIs do not parameterize the SQL, so an interpolated/concatenated value
///     built from runtime input is a SQL injection vector. A constant literal — or an interpolation
///     whose holes are all constants — is safe and not flagged. The fix is the interpolated
///     <c>FromSql</c>/<c>ExecuteSql</c> variants (auto-parameterized) or the <c>parameters</c> arg.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RawSqlInjectionAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> RawMethods = ImmutableHashSet.Create(
        "FromSqlRaw", "ExecuteSqlRaw", "ExecuteSqlRawAsync", "SqlQueryRaw");

    private static readonly ImmutableHashSet<string> ContainingTypes = ImmutableHashSet.Create(
        "Microsoft.EntityFrameworkCore.RelationalQueryableExtensions",
        "Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.RawSqlInjection);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (!RawMethods.Contains(method.Name))
            return;

        var containingType = method.ContainingType?.ToDisplayString();
        if (containingType is null || !ContainingTypes.Contains(containingType))
            return;

        // The SQL string is the parameter named "sql" on every *Raw overload.
        var sqlArgument = invocation.Arguments.FirstOrDefault(a => a.Parameter?.Name == "sql");
        if (sqlArgument is null)
            return;

        // A compile-time constant (string literal or const) is safe.
        if (sqlArgument.Value.ConstantValue.HasValue)
            return;

        // Unwrap implicit conversions (e.g. interpolated string handler → string) to reach the expression.
        var value = sqlArgument.Value;
        while (value is IConversionOperation conversion)
            value = conversion.Operand;

        // An interpolated string whose holes are all constants is also safe.
        if (value is IInterpolatedStringOperation interpolated && IsAllConstant(interpolated))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.RawSqlInjection,
            sqlArgument.Value.Syntax.GetLocation(),
            method.Name));
    }

    private static bool IsAllConstant(IInterpolatedStringOperation interpolated)
    {
        foreach (var part in interpolated.Parts)
        {
            switch (part)
            {
                case IInterpolatedStringTextOperation:
                    continue;
                case IInterpolationOperation { Expression.ConstantValue.HasValue: true }:
                    continue;
                default:
                    return false;
            }
        }

        return true;
    }
}
