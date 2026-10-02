// =============================================================================
// Pragmatic.Design - DiagnosticDescriptors Helper
// Standardized diagnostic creation for Source Generators
// =============================================================================

using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGen;

/// <summary>
///     Factory for creating standardized DiagnosticDescriptors.
///     Each module should define its own descriptors using these helpers.
/// </summary>
internal static class DiagnosticFactory
{
    // The PRAG id-range map is docs/diagnostics.md, generated from the descriptors. It is deliberately
    // NOT duplicated here: a copy in a comment drifts, and a wrong map is worse than no map.

    private const string Category = "Pragmatic.Design";

    // The docs site is docs.pragmaticdesign.net and it serves one diagnostics page, not a page per id.
    // Pointing at a per-id URL produced a dead "Learn more" link in the IDE for every diagnostic built
    // through this factory, so the link resolves to the page that actually exists.
    private const string HelpLink = "https://docs.pragmaticdesign.net/reference/diagnostics/";

    /// <summary>
    ///     Creates an error diagnostic (build failure).
    ///     Use for: Invalid attribute usage, missing requirements, incompatible code.
    /// </summary>
    public static DiagnosticDescriptor Error(string id, string title, string messageFormat, string? description = null)
    {
        return new DiagnosticDescriptor(
            id,
            title,
            messageFormat,
            Category,
            DiagnosticSeverity.Error,
            true,
            description,
            HelpLink);
    }

    /// <summary>
    ///     Creates a warning diagnostic (build succeeds with warning).
    ///     Use for: Suboptimal patterns, deprecated usage, performance concerns.
    /// </summary>
    public static DiagnosticDescriptor Warning(string id, string title, string messageFormat,
        string? description = null)
    {
        return new DiagnosticDescriptor(
            id,
            title,
            messageFormat,
            Category,
            DiagnosticSeverity.Warning,
            true,
            description,
            HelpLink);
    }

    /// <summary>
    ///     Creates an info diagnostic (informational message).
    ///     Use for: Suggestions, hints, generated code info.
    /// </summary>
    public static DiagnosticDescriptor Info(string id, string title, string messageFormat, string? description = null)
    {
        return new DiagnosticDescriptor(
            id,
            title,
            messageFormat,
            Category,
            DiagnosticSeverity.Info,
            true,
            description,
            HelpLink);
    }

    /// <summary>
    ///     Creates a hidden diagnostic (not shown but can be used for code fixes).
    ///     Use for: Refactoring opportunities, code fix triggers.
    /// </summary>
    public static DiagnosticDescriptor Hidden(string id, string title, string messageFormat, string? description = null)
    {
        return new DiagnosticDescriptor(
            id,
            title,
            messageFormat,
            Category,
            DiagnosticSeverity.Hidden,
            true,
            description,
            HelpLink);
    }
}

/// <summary>
///     Extension methods for reporting diagnostics.
/// </summary>
internal static class DiagnosticExtensions
{
    public static void ReportDiagnostic(
        this SourceProductionContext context,
        DiagnosticDescriptor descriptor,
        Location? location,
        params object?[] messageArgs)
    {
        context.ReportDiagnostic(Diagnostic.Create(descriptor, location ?? Location.None, messageArgs));
    }

    public static void ReportDiagnostic(
        this SourceProductionContext context,
        DiagnosticDescriptor descriptor,
        SyntaxNode node,
        params object?[] messageArgs)
    {
        context.ReportDiagnostic(Diagnostic.Create(descriptor, node.GetLocation(), messageArgs));
    }

    public static void ReportDiagnostic(
        this SourceProductionContext context,
        DiagnosticDescriptor descriptor,
        ISymbol symbol,
        params object?[] messageArgs)
    {
        var location = symbol.Locations.Length > 0 ? symbol.Locations[0] : Location.None;
        context.ReportDiagnostic(Diagnostic.Create(descriptor, location, messageArgs));
    }
}