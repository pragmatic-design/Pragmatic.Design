// =============================================================================
// Pragmatic.Temporal.Analyzers - DateTime.Now in Test Code Analyzer
// Detects DateTime.Now/UtcNow usage in test classes
// =============================================================================

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Pragmatic.Temporal.Analyzers;

/// <summary>
///     Analyzer that detects usage of DateTime.Now, DateTime.UtcNow,
///     DateTimeOffset.Now, or DateTimeOffset.UtcNow in test code.
///     Suggests using IClock/TestClock from Pragmatic.Temporal instead.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DateTimeNowInTestsAnalyzer : DiagnosticAnalyzer
{
    // Test framework attributes that indicate a test class or method
    private static readonly ImmutableHashSet<string> TestAttributes = ImmutableHashSet.Create(
        // xUnit
        "Fact",
        "FactAttribute",
        "Theory",
        "TheoryAttribute",
        // NUnit
        "Test",
        "TestAttribute",
        "TestCase",
        "TestCaseAttribute",
        "TestFixture",
        "TestFixtureAttribute",
        // MSTest
        "TestMethod",
        "TestMethodAttribute",
        "TestClass",
        "TestClassAttribute");

    private static readonly ImmutableHashSet<string> TimeProperties = ImmutableHashSet.Create(
        "Now",
        "UtcNow");

    private static readonly ImmutableHashSet<string> TimeTypes = ImmutableHashSet.Create(
        "DateTime",
        "DateTimeOffset");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.AvoidDateTimeNowInTests);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;
        var memberName = memberAccess.Name.Identifier.Text;

        // Check if accessing Now or UtcNow
        if (!TimeProperties.Contains(memberName))
            return;

        // Check if accessing DateTime or DateTimeOffset
        if (memberAccess.Expression is not IdentifierNameSyntax identifier)
            return;

        var typeName = identifier.Identifier.Text;
        if (!TimeTypes.Contains(typeName))
            return;

        // Verify it's actually System.DateTime or System.DateTimeOffset
        var symbol = context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol;
        if (!IsSystemDateTimeOrOffset(symbol))
            return;

        // Check if we're inside a test class
        if (!IsInTestContext(memberAccess))
            return;

        var fullName = $"{typeName}.{memberName}";
        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.AvoidDateTimeNowInTests,
            memberAccess.GetLocation(),
            fullName);
        context.ReportDiagnostic(diagnostic);
    }

    /// <summary>
    ///     Determines whether the syntax node is inside a test class or method.
    ///     Walks up the syntax tree looking for test framework attributes.
    /// </summary>
    private static bool IsInTestContext(SyntaxNode node)
    {
        var current = node.Parent;
        while (current != null)
        {
            switch (current)
            {
                // Check method-level test attributes (xUnit [Fact], [Theory], NUnit [Test], MSTest [TestMethod])
                case MethodDeclarationSyntax method:
                    if (HasTestAttribute(method.AttributeLists))
                        return true;
                    break;

                // Check class-level test attributes (NUnit [TestFixture], MSTest [TestClass])
                case ClassDeclarationSyntax classDecl:
                    if (HasTestAttribute(classDecl.AttributeLists))
                        return true;
                    // Also check if any method in the class has test attributes
                    if (ClassContainsTestMethods(classDecl))
                        return true;
                    break;
            }

            current = current.Parent;
        }

        return false;
    }

    private static bool HasTestAttribute(SyntaxList<AttributeListSyntax> attributeLists)
    {
        foreach (var attributeList in attributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var attributeName = GetAttributeName(attribute);
                if (TestAttributes.Contains(attributeName))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Checks if any method in the class has a test attribute.
    ///     This handles xUnit classes which typically have no class-level attribute.
    /// </summary>
    private static bool ClassContainsTestMethods(ClassDeclarationSyntax classDecl)
    {
        foreach (var member in classDecl.Members)
        {
            if (member is MethodDeclarationSyntax method && HasTestAttribute(method.AttributeLists))
                return true;
        }

        return false;
    }

    private static string GetAttributeName(AttributeSyntax attribute)
    {
        switch (attribute.Name)
        {
            case IdentifierNameSyntax identifierName:
                return identifierName.Identifier.Text;
            case QualifiedNameSyntax qualifiedName:
                return qualifiedName.Right.Identifier.Text;
            default:
                return string.Empty;
        }
    }

    private static bool IsSystemDateTimeOrOffset(ISymbol? symbol)
    {
        if (symbol is not IPropertySymbol property)
            return false;

        var containingType = property.ContainingType;
        if (containingType is null)
            return false;

        var namespaceName = containingType.ContainingNamespace?.ToDisplayString();
        if (namespaceName != "System")
            return false;

        return containingType.Name == "DateTime" || containingType.Name == "DateTimeOffset";
    }
}
