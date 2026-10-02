using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     Turns a generator run into a readable description of the surface it emitted: per generated file,
///     the types declared in it and the members a consumer can reach.
/// </summary>
/// <remarks>
///     <para>
///         This is the artefact the documentation is checked against. It exists because the generated
///         surface is the framework's real public API for a consumer — <c>Invoice.Expr</c>,
///         <c>Order.SoftDeleteFilter</c>, <c>GetByOrderNumberAsync</c> are all things application code
///         names — and it was the only public API in the repository with no snapshot: a template that
///         renamed a member broke every consumer and no signal in the build said so.
///     </para>
///     <para>
///         It reads the syntax the generator actually produced, not the templates that produced it.
///         A template builds its output from strings, through conditionals and loops, so anything
///         derived by reading <em>it</em> would be a guess; the emitted tree is the fact.
///     </para>
///     <para>
///         Signatures are normalised — <c>global::</c> dropped, namespaces of well-known types
///         collapsed to the simple name — because the point is the shape a reader has to know, and
///         a fully-qualified wall of text hides it. Bodies are dropped for the same reason: this
///         describes the contract, and the snapshots that already exist describe the implementation.
///     </para>
/// </remarks>
internal static class GeneratedSurfaceInventory
{
    /// <summary>
    ///     Renders the inventory for a completed generator run.
    /// </summary>
    /// <param name="trees">The syntax trees the generator produced.</param>
    /// <param name="title">Heading for the rendered document.</param>
    public static string Render(IEnumerable<SyntaxTree> trees, string title)
    {
        var sb = new StringBuilder();
        sb.Append("# ").Append(title).AppendLine();
        sb.AppendLine();
        sb.AppendLine("Each entry is a generated file: what it declares and what a consumer can call.");
        sb.AppendLine("Produced by GeneratedSurfaceInventoryTests running the generator on the corpus declared there.");

        // Ordered by hint name so the document is stable across runs: the generator's output order is
        // an implementation detail of the pipeline and would otherwise churn the snapshot.
        var ordered = trees
            .Select(t => new { Hint = HintOf(t), Tree = t })
            .OrderBy(x => x.Hint, System.StringComparer.Ordinal);

        foreach (var entry in ordered)
        {
            sb.AppendLine();
            sb.Append("## ").AppendLine(entry.Hint);

            var root = entry.Tree.GetRoot();
            var types = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().ToList();

            if (types.Count == 0)
            {
                // An assembly-attribute file (metadata, manifest) declares no type. Saying so is worth
                // a line: an empty section would read as "the generator produced nothing here".
                var attrs = root.DescendantNodes().OfType<AttributeListSyntax>()
                    .Where(a => a.Target?.Identifier.ValueText == "assembly")
                    .SelectMany(a => a.Attributes)
                    .Select(a => a.Name.ToString())
                    .Distinct(System.StringComparer.Ordinal)
                    .OrderBy(a => a, System.StringComparer.Ordinal)
                    .ToList();

                foreach (var a in attrs)
                    sb.Append("    [assembly: ").Append(a).AppendLine("]");

                if (attrs.Count == 0)
                    sb.AppendLine("    (no type declared)");

                continue;
            }

            foreach (var type in types)
                RenderType(sb, type);
        }

        return sb.ToString();
    }

    /// <summary>The generated file's hint name — the last segment of the tree's path.</summary>
    private static string HintOf(SyntaxTree tree)
    {
        var path = tree.FilePath;
        var slash = path.LastIndexOfAny(['/', '\\']);
        return slash >= 0 ? path[(slash + 1)..] : path;
    }

    private static void RenderType(StringBuilder sb, BaseTypeDeclarationSyntax type)
    {
        var indent = new string(' ', 4 * (NestingDepth(type) + 1) - 4);

        sb.Append(indent).Append(Modifiers(type.Modifiers)).Append(Keyword(type)).Append(' ').Append(NameOf(type));

        if (type.BaseList is { Types.Count: > 0 })
        {
            var bases = type.BaseList.Types.Select(b => Normalise(b.Type.ToString()));
            sb.Append(" : ").Append(string.Join(", ", bases));
        }

        sb.AppendLine();

        if (type is not TypeDeclarationSyntax decl)
            return;

        foreach (var line in Members(decl))
        {
            sb.Append(indent).Append("    ").AppendLine(line);

            // The EF model is configured in a body, not in a signature. Everywhere else a body is
            // implementation and listing it would bury the contract — here the body IS the contract:
            // the delete behaviour of a relation, the named query filter, the partial unique index on
            // a soft-delete logic key are facts a consumer has to know and a document has to state,
            // and none of them appears in the method's signature.
            if (line.EndsWith(")", System.StringComparison.Ordinal) && ConfiguresTheModel(line))
            {
                foreach (var call in ConfigurationCalls(decl, line))
                    sb.Append(indent).Append("        ").AppendLine(call);
            }
        }
    }

    /// <summary>The methods whose body describes the database or the query rather than the behaviour.</summary>
    /// <remarks>
    ///     <c>ApplyIncludes</c> joined the two EF ones for the same reason and after the same surprise:
    ///     its signature never changes, so a loading profile that silently included half the navigations
    ///     looked identical in the inventory to one that included them all.
    /// </remarks>
    private static bool ConfiguresTheModel(string signature)
        => signature.Contains(" Configure(", System.StringComparison.Ordinal)
            || signature.Contains(" OnModelCreating(", System.StringComparison.Ordinal)
            || signature.Contains(" ApplyIncludes(", System.StringComparison.Ordinal);

    /// <summary>
    ///     The fluent EF calls in that method, one statement per line, normalised the same way as a
    ///     signature. Argument lambdas are kept — <c>e =&gt; !e.IsDeleted</c> is the filter — while the
    ///     surrounding noise is not.
    /// </summary>
    private static IEnumerable<string> ConfigurationCalls(TypeDeclarationSyntax type, string signature)
    {
        var method = type.Members
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => signature.Contains($" {m.Identifier.ValueText}(", System.StringComparison.Ordinal));

        if (method?.Body is null)
            yield break;

        foreach (var statement in method.Body.Statements)
        {
            if (statement is not ExpressionStatementSyntax expression)
                continue;

            // One line, whatever the generator's own line breaks: a fluent chain split across five
            // lines and the same chain on one are the same fact, and a snapshot that treats them as
            // different churns on reformatting.
            var text = string.Join(" ", expression.Expression.ToString()
                .Split(['\r', '\n'], System.StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim()));

            // Only the global:: qualifier comes off. NOT the type-simplifying pass used on signatures:
            // that one drops everything before a dot, and on an expression it eats the dots of the
            // fluent chain and the receiver of every member access — "e.TenantId == _ctx.TenantId"
            // came out as "TenantId == TenantId", which is not a shorter way of saying the same thing,
            // it is a different and false statement.
            yield return StripGlobalQualifier(text) + ";";
        }
    }

    /// <summary>
    ///     Members a consumer can reach: public and internal. Private ones are implementation, and a
    ///     document that listed them would hide the contract in the noise.
    /// </summary>
    private static IEnumerable<string> Members(TypeDeclarationSyntax type)
    {
        foreach (var member in type.Members)
        {
            // Nested types are rendered as types in their own right by the caller's walk.
            if (member is BaseTypeDeclarationSyntax)
                continue;

            if (!IsReachable(member))
                continue;

            var text = Describe(member);
            if (text is not null)
                yield return text;
        }
    }

    private static bool IsReachable(MemberDeclarationSyntax member)
    {
        var mods = member.Modifiers;
        if (mods.Any(SyntaxKind.PrivateKeyword))
            return false;

        // No accessibility keyword inside a class means private. The generator is explicit almost
        // everywhere; where it is not, the member is not part of the contract.
        return mods.Any(SyntaxKind.PublicKeyword)
            || mods.Any(SyntaxKind.InternalKeyword)
            || mods.Any(SyntaxKind.ProtectedKeyword);
    }

    private static string? Describe(MemberDeclarationSyntax member) => member switch
    {
        PropertyDeclarationSyntax p =>
            $"{Modifiers(p.Modifiers)}{Normalise(p.Type.ToString())} {p.Identifier.ValueText} {Accessors(p)}",

        MethodDeclarationSyntax m =>
            $"{Modifiers(m.Modifiers)}{Normalise(m.ReturnType.ToString())} {m.Identifier.ValueText}{TypeParams(m)}({Parameters(m.ParameterList)})",

        ConstructorDeclarationSyntax c =>
            $"{Modifiers(c.Modifiers)}{c.Identifier.ValueText}({Parameters(c.ParameterList)})",

        FieldDeclarationSyntax f =>
            $"{Modifiers(f.Modifiers)}{Normalise(f.Declaration.Type.ToString())} {string.Join(", ", f.Declaration.Variables.Select(v => v.Identifier.ValueText))}",

        EventFieldDeclarationSyntax e =>
            $"{Modifiers(e.Modifiers)}event {Normalise(e.Declaration.Type.ToString())} {string.Join(", ", e.Declaration.Variables.Select(v => v.Identifier.ValueText))}",

        OperatorDeclarationSyntax o =>
            $"{Modifiers(o.Modifiers)}{Normalise(o.ReturnType.ToString())} operator {o.OperatorToken.ValueText}({Parameters(o.ParameterList)})",

        _ => null
    };

    private static string TypeParams(MethodDeclarationSyntax m)
        => m.TypeParameterList is null ? string.Empty : m.TypeParameterList.ToString();

    private static string Accessors(PropertyDeclarationSyntax p)
    {
        if (p.ExpressionBody is not null)
            return "{ get; }";

        if (p.AccessorList is null)
            return string.Empty;

        var parts = p.AccessorList.Accessors.Select(a =>
        {
            var mods = a.Modifiers.Count > 0 ? Modifiers(a.Modifiers) : string.Empty;
            return $"{mods}{a.Keyword.ValueText};";
        });

        return $"{{ {string.Join(" ", parts)} }}";
    }

    private static string Parameters(BaseParameterListSyntax? list)
    {
        if (list is null || list.Parameters.Count == 0)
            return string.Empty;

        return string.Join(", ", list.Parameters.Select(p =>
        {
            var modifiers = p.Modifiers.Count > 0 ? Modifiers(p.Modifiers) : string.Empty;
            var type = p.Type is null ? string.Empty : Normalise(p.Type.ToString()) + " ";
            // The default value is part of the contract: whether ct is optional decides call sites.
            var def = p.Default is null ? string.Empty : " = " + Normalise(p.Default.Value.ToString());
            return $"{modifiers}{type}{p.Identifier.ValueText}{def}";
        }));
    }

    private static string Modifiers(SyntaxTokenList modifiers)
    {
        // Ordered as written; "partial" carried because it says the type is completed elsewhere, which
        // is the whole shape of this framework.
        var kept = modifiers
            .Select(m => m.ValueText)
            .Where(m => m is not ("unsafe" or "extern"))
            .ToList();

        return kept.Count == 0 ? string.Empty : string.Join(" ", kept) + " ";
    }

    private static string Keyword(BaseTypeDeclarationSyntax type) => type switch
    {
        ClassDeclarationSyntax => "class",
        StructDeclarationSyntax => "struct",
        InterfaceDeclarationSyntax => "interface",
        RecordDeclarationSyntax r => r.ClassOrStructKeyword.ValueText is "struct" ? "record struct" : "record",
        EnumDeclarationSyntax => "enum",
        _ => "type"
    };

    /// <summary>The type's name qualified by the types it is nested in, so a nested class reads as a caller writes it.</summary>
    private static string NameOf(BaseTypeDeclarationSyntax type)
    {
        var parts = new List<string> { type.Identifier.ValueText + TypeParamsOf(type) };

        for (var parent = type.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is BaseTypeDeclarationSyntax outer)
                parts.Insert(0, outer.Identifier.ValueText + TypeParamsOf(outer));
        }

        return string.Join(".", parts);
    }

    private static string TypeParamsOf(BaseTypeDeclarationSyntax type)
        => type is TypeDeclarationSyntax { TypeParameterList: { } tp } ? tp.ToString() : string.Empty;

    private static int NestingDepth(SyntaxNode node)
    {
        var depth = 0;
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is BaseTypeDeclarationSyntax)
                depth++;
        }

        return depth;
    }

    /// <summary>
    ///     Drops <c>global::</c> and the namespace of every type, leaving the simple name.
    /// </summary>
    /// <remarks>
    ///     A generated signature is fully qualified by construction — that is how generated code stays
    ///     correct wherever it lands — and reading it that way is the reason nobody reads it. The
    ///     simple name is what a consumer writes, and the file it appears in says which namespace it
    ///     came from.
    /// </remarks>
    private static string Normalise(string type)
    {
        var text = type.Replace("global::", string.Empty);
        var sb = new StringBuilder(text.Length);
        var segment = new StringBuilder();

        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c) || c == '_' || c == '.')
            {
                segment.Append(c);
                continue;
            }

            sb.Append(Simplify(segment.ToString()));
            segment.Clear();
            sb.Append(c);
        }

        sb.Append(Simplify(segment.ToString()));
        return sb.ToString();
    }

    /// <summary>
    ///     Drops <c>global::</c> and the namespace that follows it, leaving the type name and every
    ///     dot that carries meaning. Used on expressions, where the dots are the structure.
    /// </summary>
    private static string StripGlobalQualifier(string text)
        => System.Text.RegularExpressions.Regex.Replace(text, @"global::([\w]+\.)*", string.Empty);

    private static string Simplify(string qualified)
    {
        if (qualified.Length == 0 || !qualified.Contains('.'))
            return qualified;

        // A trailing dot belongs to the text around the identifier, not to it.
        var dot = qualified.LastIndexOf('.');
        return dot == qualified.Length - 1 ? qualified : qualified[(dot + 1)..];
    }
}
