using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Pragmatic.SourceGenerator.Analyzers.Tests.Suppressors;

/// <summary>
///     Emits one warning with a caller-chosen id (CA1062, CA1822, IDE0051, …) on every method,
///     property and field declaration, using the member name as the message.
///     The suppressors under test only read <see cref="Diagnostic.Id" /> and
///     <see cref="Diagnostic.Location" />, so a stand-in producer exercises them faithfully without
///     pulling the real analyzer packages (CA1062 is not even enabled by default in this repo).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class MemberDiagnosticProducer : DiagnosticAnalyzer
{
    private readonly DiagnosticDescriptor _descriptor;

    public MemberDiagnosticProducer(string id)
        => _descriptor = new DiagnosticDescriptor(
            id, id, "{0}", "Test", DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(_descriptor);

    public override void Initialize(AnalysisContext context)
    {
        // The generated-code default would drop every diagnostic inside *.g.cs — exactly the
        // location the suppressors must still be able to see.
        context.ConfigureGeneratedCodeAnalysis(
            GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(
            Report,
            SyntaxKind.MethodDeclaration,
            SyntaxKind.PropertyDeclaration,
            SyntaxKind.FieldDeclaration);
    }

    private void Report(SyntaxNodeAnalysisContext context)
    {
        switch (context.Node)
        {
            case MethodDeclarationSyntax method:
                context.ReportDiagnostic(Diagnostic.Create(
                    _descriptor, method.Identifier.GetLocation(), method.Identifier.Text));
                break;

            case PropertyDeclarationSyntax property:
                context.ReportDiagnostic(Diagnostic.Create(
                    _descriptor, property.Identifier.GetLocation(), property.Identifier.Text));
                break;

            case FieldDeclarationSyntax field:
                foreach (var variable in field.Declaration.Variables)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        _descriptor, variable.Identifier.GetLocation(), variable.Identifier.Text));
                }

                break;
        }
    }
}
