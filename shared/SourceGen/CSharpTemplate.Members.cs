// =============================================================================
// Pragmatic.Design - CSharpTemplate: Member Generation Methods
// Field, Property, Method, Constructor, Operators
// =============================================================================

using System.Text;

namespace Pragmatic.SourceGen;

internal abstract partial class CSharpTemplate
{
    // =============================================================================
    // Members: Field, Property, Method, Constructor
    // =============================================================================

    protected void Field(string fieldName, string type, AccessModifier accessModifier = AccessModifier.Private,
        bool isReadOnly = false, bool isStatic = false, string? initializer = null)
    {
        var mod = accessModifier.ToKeyword();
        var stat = isStatic ? "static " : "";
        var ro = isReadOnly ? "readonly " : "";
        var init = !string.IsNullOrEmpty(initializer) ? $" = {initializer}" : "";
        AppendLine($"{mod} {stat}{ro}{type} {IdentifierHelper.EscapeIfKeyword(fieldName)}{init};");
    }

    protected void Property(string propertyName, string type, AccessModifier accessModifier = AccessModifier.Public,
        bool isStatic = false, bool isReadOnly = false, string? defaultValue = null,
        Action? getBody = null, Action? setBody = null)
    {
        var mod = $"{accessModifier.ToKeyword()} ";
        if (isStatic)
            mod += "static ";
        if (isReadOnly)
            mod += "readonly ";

        if (getBody == null && setBody == null)
        {
            // Auto property
            var init = !string.IsNullOrEmpty(defaultValue) ? $" = {defaultValue};" : "";
            AppendLine($"{mod}{type} {IdentifierHelper.EscapeIfKeyword(propertyName)} {{ get; set; }}{init}");
        }
        else
        {
            // Property with body
            AppendLine($"{mod}{type} {IdentifierHelper.EscapeIfKeyword(propertyName)}");
            Block(() =>
            {
                if (getBody != null)
                    Block(getBody, "get");
                if (setBody != null)
                    Block(setBody, "set");
            });
        }
    }

    /// <summary>
    ///     Expression-bodied property. Note: attributes like [MethodImpl] are NOT valid on properties.
    ///     If you need [MethodImpl], use PropertyWithGetter and put the attribute inside the getter.
    /// </summary>
    protected void ExpressionProperty(string propertyName, string type, string expression,
        AccessModifier accessModifier = AccessModifier.Public, bool isStatic = false)
    {
        var mod = $"{accessModifier.ToKeyword()} ";
        if (isStatic)
            mod += "static ";
        AppendLine($"{mod}{type} {IdentifierHelper.EscapeIfKeyword(propertyName)} => {expression};");
    }

    /// <summary>
    ///     Creates a property with a custom getter body. The body should output the full getter line(s)
    ///     including any attributes. Example: AppendLine("[MethodImpl(...)]"); AppendLine("get => expr;");
    /// </summary>
    protected void PropertyWithGetter(string propertyName, string type, Action getterLines,
        AccessModifier accessModifier = AccessModifier.Public, bool isStatic = false)
    {
        var mod = $"{accessModifier.ToKeyword()} ";
        if (isStatic)
            mod += "static ";
        AppendLine($"{mod}{type} {IdentifierHelper.EscapeIfKeyword(propertyName)}");
        Block(getterLines);
    }

    protected void Method(string methodName, Action body, string returnType,
        List<MethodParameter>? parameters = null, AccessModifier accessModifier = AccessModifier.Public,
        MethodModifiers modifiers = default, string? attribute = null)
    {
        if (parent != null)
        {
            parent.Method(methodName, body, returnType, parameters, accessModifier, modifiers, attribute);
            return;
        }

        if (!string.IsNullOrEmpty(attribute))
            AppendLine($"[{attribute}]");

        var mod = BuildMethodModifiers(accessModifier, modifiers);
        var name = IdentifierHelper.EscapeIfKeyword(methodName);

        if (parameters == null || parameters.Count == 0)
            AppendLine($"{mod}{returnType} {name}()");
        else
            AppendParameters($"{mod}{returnType} {name}(", parameters, ")");

        Block(body);
        AppendLine();
    }

    protected void ExpressionMethod(string methodName, string expression, string returnType,
        List<MethodParameter>? parameters = null, AccessModifier accessModifier = AccessModifier.Public,
        MethodModifiers modifiers = default, string? attribute = null)
    {
        if (!string.IsNullOrEmpty(attribute))
            AppendLine($"[{attribute}]");

        var mod = BuildMethodModifiers(accessModifier, modifiers);
        var paramStr = parameters == null || parameters.Count == 0
            ? "()"
            : $"({string.Join(", ", parameters.Select(p => p.ToString()))})";

        AppendLine($"{mod}{returnType} {IdentifierHelper.EscapeIfKeyword(methodName)}{paramStr} => {expression};");
    }

    /// <summary>
    ///     Creates a generic method with type parameters.
    /// </summary>
    /// <remarks>
    ///     <c>constraints</c> is written without the leading <c>where</c> — e.g.
    ///     <c>"TRelated : class"</c>. One string rather than a list: two constraints are two clauses,
    ///     and the caller writing them knows how they are separated.
    /// </remarks>
    protected void GenericMethod(string methodName, List<string> typeParams, Action body, string returnType,
        List<MethodParameter>? parameters = null, AccessModifier accessModifier = AccessModifier.Public,
        MethodModifiers modifiers = default, string? attribute = null, string? constraints = null)
    {
        if (!string.IsNullOrEmpty(attribute))
            AppendLine($"[{attribute}]");

        var mod = BuildMethodModifiers(accessModifier, modifiers);
        var typeParamStr = $"<{string.Join(", ", typeParams)}>";
        var name = IdentifierHelper.EscapeIfKeyword(methodName);

        if (parameters == null || parameters.Count == 0)
            AppendLine($"{mod}{returnType} {name}{typeParamStr}()");
        else
            AppendParameters($"{mod}{returnType} {name}{typeParamStr}(", parameters, ")");

        if (!string.IsNullOrEmpty(constraints))
        {
            IncreaseIndent();
            AppendLine($"where {constraints}");
            DecreaseIndent();
        }

        Block(body);
        AppendLine();
    }

    protected void Constructor(string className, Action body, List<MethodParameter>? parameters = null,
        AccessModifier accessModifier = AccessModifier.Public, string? baseCall = null, bool isStatic = false)
    {
        if (parent != null)
        {
            parent.Constructor(className, body, parameters, accessModifier, baseCall, isStatic);
            return;
        }

        var stat = isStatic ? "static " : "";
        var basePart = !string.IsNullOrEmpty(baseCall) ? $" : base({baseCall})" : "";
        var name = IdentifierHelper.EscapeIfKeyword(className);

        if (parameters == null || parameters.Count == 0)
            AppendLine($"{accessModifier.ToKeyword()} {stat}{name}(){basePart}");
        else
            AppendParameters($"{accessModifier.ToKeyword()} {stat}{name}(", parameters, $"){basePart}");

        Block(body);
        AppendLine();
    }

    private static string BuildMethodModifiers(AccessModifier access, MethodModifiers mod)
    {
        var sb = new StringBuilder();
        if (access != AccessModifier.NotApplicable)
            sb.Append($"{access.ToKeyword()} ");
        if (mod.IsStatic)
            sb.Append("static ");
        if (mod.IsVirtual)
            sb.Append("virtual ");
        if (mod.IsOverride)
            sb.Append("override ");
        if (mod.IsAbstract)
            sb.Append("abstract ");
        if (mod.IsSealed)
            sb.Append("sealed ");
        if (mod.IsAsync)
            sb.Append("async ");
        return sb.ToString();
    }

    // =============================================================================
    // Operators
    // =============================================================================

    /// <summary>
    ///     Creates an implicit conversion operator.
    /// </summary>
    /// <param name="paramDeclaration">Full parameter declaration e.g. "TValue value"</param>
    /// <param name="toType">Target type</param>
    /// <param name="expression">Conversion expression</param>
    /// <param name="attribute">Optional attribute (e.g. MethodImpl)</param>
    protected void ImplicitOperator(string paramDeclaration, string toType, string expression, string? attribute = null)
    {
        if (!string.IsNullOrEmpty(attribute))
            AppendLine($"[{attribute}]");
        AppendLine($"public static implicit operator {toType}({paramDeclaration}) => {expression};");
    }

    // =============================================================================
    // Parameter helpers
    // =============================================================================

    private void AppendParameters(string initialLine, List<MethodParameter> parameters, string endingChar)
    {
        Append(initialLine);
        PushCustomIndentation();

        for (var i = 0; i < parameters.Count; i++)
        {
            Append(parameters[i].ToString());
            if (i < parameters.Count - 1)
                Append(", ");
            else
                Append(endingChar);
            AppendLine();
        }

        PopCustomIndentation();
    }
}
