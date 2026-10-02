using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Pragmatic.SourceGenerator.Analyzers;

/// <summary>
///     Checks <c>[VisibleWhen&lt;TRule&gt;]</c> on an entity, <c>[WithoutFilter&lt;T&gt;]</c> on an
///     operation, and <c>Disable&lt;TRule&gt;()</c> at a call site (<c>PRAG0717</c>–<c>PRAG0721</c>).
/// </summary>
/// <remarks>
///     Attributes are matched on name, arity and namespace rather than on
///     <c>ToDisplayString()</c>: a generic attribute renders with its argument inside it, so comparing
///     the whole string never matches — the mistake that left one branch of <c>[ExposeEndpoint]</c>
///     unreachable for months.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class VisibilityRuleAnalyzer : DiagnosticAnalyzer
{
    private const string EntityNamespace = "Pragmatic.Persistence.Entity";
    private const string FiltersNamespace = "Pragmatic.Persistence.Query.Filters";
    private const string RuleBase = "Pragmatic.Persistence.Query.Filters.VisibilityRule`1";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            VisibilityRuleDescriptors.WrongEntity,
            VisibilityRuleDescriptors.NotConstructible,
            VisibilityRuleDescriptors.WithoutFilterNeedsPermission,
            VisibilityRuleDescriptors.DisableByTypeDoesNothing,
            VisibilityRuleDescriptors.WriteNeedsWithoutFilter);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        var ruleBase = context.Compilation.GetTypeByMetadataName(RuleBase);
        if (ruleBase is null)
            return;

        context.RegisterSymbolAction(c => Analyze(c, ruleBase), SymbolKind.NamedType);
        context.RegisterOperationAction(c => AnalyzeDisableCall(c, ruleBase), OperationKind.Invocation);
    }

    private static void Analyze(SymbolAnalysisContext context, INamedTypeSymbol ruleBase)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        if (MutatedEntity(type) is { } mutated)
            CheckWrite(context, type, mutated, ruleBase);

        foreach (var attribute in type.GetAttributes())
        {
            var attributeClass = attribute.AttributeClass;
            if (attributeClass is not { TypeArguments.Length: 1 })
                continue;

            if (attributeClass.TypeArguments[0] is not INamedTypeSymbol rule)
                continue;

            var ns = attributeClass.ContainingNamespace?.ToDisplayString();

            if (attributeClass.Name == "VisibleWhenAttribute" && ns == EntityNamespace)
                CheckDeclaration(context, attribute, type, rule, ruleBase);
            else if (attributeClass.Name == "WithoutFilterAttribute" && ns == FiltersNamespace)
                CheckLift(context, attribute, type, rule);
        }
    }

    /// <summary>[VisibleWhen&lt;TRule&gt;]: the rule must fit this entity, and be constructible.</summary>
    private static void CheckDeclaration(
        SymbolAnalysisContext context,
        AttributeData attribute,
        INamedTypeSymbol entity,
        INamedTypeSymbol rule,
        INamedTypeSymbol ruleBase)
    {
        var location = LocationOf(attribute, entity, context);
        var filtered = FilteredEntityOf(rule, ruleBase);

        if (filtered is null || !SymbolEqualityComparer.Default.Equals(filtered, entity))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                VisibilityRuleDescriptors.WrongEntity, location, entity.Name, rule.Name));
            return;
        }

        // Public and parameterless, because the entity configuration writes `new TRule()` while EF
        // builds the model — no container to ask, and no arguments to give it.
        if (rule.IsAbstract
            || rule.IsGenericType
            || !rule.InstanceConstructors.Any(c =>
                c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length == 0))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                VisibilityRuleDescriptors.NotConstructible, location, rule.Name));
        }
    }

    /// <summary>
    ///     [WithoutFilter&lt;T&gt;]: the operation must declare the privilege that lifting a filter is.
    /// </summary>
    /// <remarks>
    ///     The type argument may be an entity — lifting every filter it has — or a single filter type,
    ///     because <c>IQueryFilterToggle.Disable(Type)</c> matches on either. Both are a privilege, so
    ///     neither is checked here beyond the permission: which filter is lifted changes how much is
    ///     revealed, not whether revealing it needs saying.
    /// </remarks>
    private static void CheckLift(
        SymbolAnalysisContext context,
        AttributeData attribute,
        INamedTypeSymbol operation,
        INamedTypeSymbol lifted)
    {
        if (HasPermission(operation))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            VisibilityRuleDescriptors.WithoutFilterNeedsPermission,
            LocationOf(attribute, operation, context),
            operation.Name,
            lifted.Name));
    }

    /// <summary>The entity a rule filters: the argument of the VisibilityRule&lt;T&gt; it derives from.</summary>
    private static INamedTypeSymbol? FilteredEntityOf(INamedTypeSymbol rule, INamedTypeSymbol ruleBase)
    {
        for (var current = rule.BaseType; current is not null; current = current.BaseType)
        {
            if (!SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, ruleBase))
                continue;

            return current.TypeArguments.Length == 1
                ? current.TypeArguments[0] as INamedTypeSymbol
                : null;
        }

        return null;
    }

    private static bool HasPermission(INamedTypeSymbol operation)
        => operation.GetAttributes().Any(a => a.AttributeClass?.Name == "RequirePermissionAttribute");

    private static Location LocationOf(
        AttributeData attribute, INamedTypeSymbol fallback, SymbolAnalysisContext context)
        => attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken)?.GetLocation()
           ?? fallback.Locations.FirstOrDefault()
           ?? Location.None;

    /// <summary>
    ///     <c>Disable&lt;TRule&gt;()</c> / <c>Disable(typeof(TRule))</c> on a declared rule.
    /// </summary>
    /// <remarks>
    ///     Both overloads are checked, and the generic argument is read from the method symbol rather
    ///     than from the syntax, so an alias or a fully qualified name is seen the same way.
    /// </remarks>
    private static void AnalyzeDisableCall(OperationAnalysisContext context, INamedTypeSymbol ruleBase)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (method.Name != "Disable"
            || method.ContainingType is not { Name: "IQueryFilterToggle" } owner
            || owner.ContainingNamespace?.ToDisplayString() != FiltersNamespace)
        {
            return;
        }

        var argument = method.TypeArguments.Length == 1
            ? method.TypeArguments[0] as INamedTypeSymbol
            : TypeOfArgument(invocation);

        if (argument is null || FilteredEntityOf(argument, ruleBase) is null)
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            VisibilityRuleDescriptors.DisableByTypeDoesNothing,
            invocation.Syntax.GetLocation(),
            argument.Name));
    }

    /// <summary>The type inside a single <c>typeof(...)</c> argument, when that is what was passed.</summary>
    private static INamedTypeSymbol? TypeOfArgument(IInvocationOperation invocation)
        => invocation.Arguments.Length == 1
           && invocation.Arguments[0].Value is ITypeOfOperation typeOf
            ? typeOf.TypeOperand as INamedTypeSymbol
            : null;

    /// <summary>
    ///     An operation writing the property its entity's rule keys on, with no way past the rule.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Only fires when both halves are legible: the rule's <c>ToExpression()</c> is a plain
    ///         member access on its parameter, so the property has a name, and the operation declares a
    ///         settable member of that name. Anything cleverer in the predicate leaves this silent
    ///         rather than guessing.
    ///     </para>
    /// </remarks>
    private static void CheckWrite(
        SymbolAnalysisContext context,
        INamedTypeSymbol operation,
        INamedTypeSymbol entity,
        INamedTypeSymbol ruleBase)
    {
        foreach (var rule in DeclaredRules(entity))
        {
            if (FilteredEntityOf(rule, ruleBase) is null)
                continue;

            if (KeyedProperty(rule) is not { } property)
                continue;

            if (!WritesProperty(operation, property))
                continue;

            if (LiftsRule(operation, rule))
                continue;

            context.ReportDiagnostic(Diagnostic.Create(
                VisibilityRuleDescriptors.WriteNeedsWithoutFilter,
                operation.Locations.FirstOrDefault() ?? Location.None,
                operation.Name,
                property,
                rule.Name));
        }
    }

    /// <summary>
    ///     The entity a mutation writes: the first argument of the <c>Mutation&lt;TEntity, …&gt;</c> it
    ///     derives from.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Read from the base class and not from <c>[Mutation]</c>, which is <b>not</b> generic — it
    ///     carries a <c>Mode</c> and nothing else. Matching on an attribute type argument here would
    ///     have produced a branch that never runs, which is how one branch of <c>[ExposeEndpoint]</c>
    ///     went unexecuted for months.
    ///
    ///     Mutations only. A <c>DomainAction&lt;Guid&gt;</c> names an identifier, not an entity, so
    ///     there is nothing to compare a rule against.
    /// </remarks>
    private static INamedTypeSymbol? MutatedEntity(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.Name == "Mutation"
                && current.ContainingNamespace?.ToDisplayString() == "Pragmatic.Actions.Mutation"
                && current.TypeArguments.Length >= 1)
            {
                return current.TypeArguments[0] as INamedTypeSymbol;
            }
        }

        return null;
    }

    /// <summary>The rules an entity declares with <c>[VisibleWhen&lt;TRule&gt;]</c>.</summary>
    private static IEnumerable<INamedTypeSymbol> DeclaredRules(INamedTypeSymbol entity)
    {
        foreach (var attribute in entity.GetAttributes())
        {
            if (attribute.AttributeClass is not { Name: "VisibleWhenAttribute", TypeArguments.Length: 1 } declared)
                continue;

            if (declared.ContainingNamespace?.ToDisplayString() != EntityNamespace)
                continue;

            if (declared.TypeArguments[0] is INamedTypeSymbol rule)
                yield return rule;
        }
    }

    /// <summary>
    ///     The single property a rule's predicate reads, when it reads exactly one and reads it plainly.
    /// </summary>
    private static string? KeyedProperty(INamedTypeSymbol rule)
    {
        var method = rule.GetMembers("ToExpression").OfType<IMethodSymbol>().FirstOrDefault();
        if (method?.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not MethodDeclarationSyntax syntax)
            return null;

        var body = syntax.ExpressionBody?.Expression ?? SingleReturned(syntax);
        if (body is not SimpleLambdaExpressionSyntax lambda)
            return null;

        return lambda.ExpressionBody switch
        {
            MemberAccessExpressionSyntax direct => direct.Name.Identifier.ValueText,
            PrefixUnaryExpressionSyntax { Operand: MemberAccessExpressionSyntax negated }
                => negated.Name.Identifier.ValueText,
            _ => null
        };
    }

    /// <summary>The expression of a single-<c>return</c> body, when that is what the method has.</summary>
    /// <remarks>
    ///     Written without a list pattern: this analyzer compiles to netstandard2.0, where
    ///     <c>System.Index</c> does not exist and the indexer pattern cannot be lowered.
    /// </remarks>
    private static ExpressionSyntax? SingleReturned(MethodDeclarationSyntax syntax)
    {
        var statements = syntax.Body?.Statements;
        if (statements is not { Count: 1 })
            return null;

        return statements.Value[0] is ReturnStatementSyntax returned ? returned.Expression : null;
    }

    /// <summary>Whether the operation declares a settable member of that name.</summary>
    private static bool WritesProperty(INamedTypeSymbol operation, string property)
        => operation.GetMembers(property).OfType<IPropertySymbol>().Any(p => p.SetMethod is not null);

    /// <summary>Whether the operation already declares it may see past this rule.</summary>
    private static bool LiftsRule(INamedTypeSymbol operation, INamedTypeSymbol rule)
    {
        foreach (var attribute in operation.GetAttributes())
        {
            if (attribute.AttributeClass is not { Name: "WithoutFilterAttribute", TypeArguments.Length: 1 } lift)
                continue;

            if (lift.ContainingNamespace?.ToDisplayString() != FiltersNamespace)
                continue;

            if (SymbolEqualityComparer.Default.Equals(lift.TypeArguments[0], rule))
                return true;
        }

        return false;
    }
}
