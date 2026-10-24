using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

/// <summary>
///     A lambda of a chain into conditions on the document's fields: comparisons of a field with a value, <c>&amp;&amp;</c> <c>||</c> <c>!</c>,
///     null, a boolean field, <c>Contains</c>/<c>StartsWith</c>/<c>EndsWith</c> on a string, a list of values' <c>Contains</c> (<c>$in</c>),
///     <c>Any</c> on an embedded array (<c>$elemMatch</c>). A value is a constant, or the two-parameter form's argument and its members (code
///     over the chain's slots); a value of the enclosing method is PRAG3102. Anything else is PRAG3101 with what the subset is.
/// </summary>
internal sealed partial class DocTranslator
{
    private readonly SemanticModel _model;
    private readonly DocModel? _root; // null: the elements of an array of values (the parameter is the value)
    private readonly Func<INamedTypeSymbol, DocModel?> _models;
    private readonly IParameterSymbol _parameter;
    private readonly IParameterSymbol? _argument;
    private readonly string? _argumentCode;
    private readonly List<Diagnostic> _diagnostics;
    private readonly Location _where;
    private readonly IReadOnlyDictionary<SyntaxNode, string>? _treePaths; // a specification's lambda: where each part is in its expression tree
    private readonly List<string>? _captures; // a specification's captured values, as the code that reads each off the tree

    /// <remarks>
    ///     With tree paths and a captures list, a specification's lambda: a captured value (a local, a parameter, a member of the enclosing type) is
    ///     not refused but read off the expression tree when the specification is built (<see cref="DocTreePaths" />), and taken from the slots.
    /// </remarks>
    public DocTranslator(SemanticModel model, DocModel? root, Func<INamedTypeSymbol, DocModel?> models, IParameterSymbol parameter, IParameterSymbol? argument, string? argumentCode, List<Diagnostic> diagnostics, Location where,
        IReadOnlyDictionary<SyntaxNode, string>? treePaths = null, List<string>? captures = null)
    {
        _model = model;
        _root = root;
        _models = models;
        _parameter = parameter;
        _argument = argument;
        _argumentCode = argumentCode;
        _diagnostics = diagnostics;
        _where = where;
        _treePaths = treePaths;
        _captures = captures;
    }

    public bool Failed { get; private set; }

    public DocCondition? Condition(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case ParenthesizedExpressionSyntax paren:
                return Condition(paren.Expression);
            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.LogicalAndExpression) || binary.IsKind(SyntaxKind.LogicalOrExpression):
            {
                var op = binary.IsKind(SyntaxKind.LogicalAndExpression) ? "$and" : "$or";
                var left = Condition(binary.Left);
                var right = Condition(binary.Right);
                if (left is null || right is null)
                    return null;
                var items = new List<DocCondition>();
                foreach (var side in new[] { left, right })
                {
                    if (side is DocLogical logical && logical.Op == op)
                        items.AddRange(logical.Items); // a && b && c: one $and
                    else
                        items.Add(side);
                }

                return new DocLogical(op, items);
            }

            case PrefixUnaryExpressionSyntax not when not.IsKind(SyntaxKind.LogicalNotExpression):
            {
                if (Path(not.Operand, out var flag) is { } flagPath && flag?.Kind == DocKind.Boolean)
                    return new DocCondition.Compare(flagPath, "$ne", new DocValue("true"));
                var inner = Condition(not.Operand);
                return inner is null ? null : new DocLogical("$nor", [inner]);
            }

            case BinaryExpressionSyntax comparison when Operator(comparison.Kind()) is { } op:
                return Comparison(comparison, op);
            case InvocationExpressionSyntax invocation:
                return Invocation(invocation);
            case MemberAccessExpressionSyntax { Name.Identifier.ValueText: "HasValue" } hasValue when Path(hasValue.Expression, out _) is { } nullablePath:
                return new DocCondition.Compare(nullablePath, "$ne", DocValue.Null);
        }

        if (Path(expression, out var field) is { } path && field?.Kind == DocKind.Boolean)
            return new DocCondition.Compare(path, "$eq", new DocValue("true"));
        return Fail(expression, "a condition compares a field with a value (== != < <= > >=), combines conditions (&& || !), tests a boolean field, or calls Contains, StartsWith, EndsWith, Any");
    }

    private DocCondition? Comparison(BinaryExpressionSyntax comparison, string op)
    {
        if (LoweredComparison(comparison, op, out var lowered))
            return lowered;
        var leftPath = Path(comparison.Left, out var leftField);
        var rightPath = Path(comparison.Right, out var rightField);
        if (leftPath is not null && rightPath is not null)
            return Fail(comparison, "a comparison of two fields is not translated (MongoDB needs $expr): compare a field with a value");
        var (path, field, valueSide, flipped) = leftPath is not null ? (leftPath, leftField, comparison.Right, false) : (rightPath, rightField, comparison.Left, true);
        if (path is null || (field is null && path.Length > 0))
            return Fail(comparison, "one side of a comparison is a field of the document");
        var value = Value(valueSide, field);
        if (value is null)
            return null;
        if (field?.Kind is DocKind.Embedded or DocKind.List && !value.IsNull)
            return Fail(comparison, $"'{field.Property.Name}' is a nested document or an array: compare one of its fields with a value, it with null, or use Any");
        if (value.IsNull && op is not ("$eq" or "$ne"))
            return Fail(comparison, "null is compared with == or !=");
        return new DocCondition.Compare(path, flipped ? Flip(op) : op, value);
    }

    private DocCondition? Invocation(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax access || _model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
            return Fail(invocation, "the call is not one of Contains, StartsWith, EndsWith, Any");
        var arguments = invocation.ArgumentList.Arguments;
        var name = method.Name;
        // A string field's text: Contains, StartsWith, EndsWith with one argument (ordinal, case-sensitive, as LINQ's default).
        if (name is "Contains" or "StartsWith" or "EndsWith" && method.ContainingType.SpecialType == SpecialType.System_String)
        {
            if (arguments.Count != 1)
                return Fail(invocation, $"{name} takes the text alone (no StringComparison: the match is ordinal, case-sensitive)");
            var loweredPath = LoweredPath(access.Expression, out var textField); // e.Text.ToLower().StartsWith(v.ToLower())
            var ignoreCase = loweredPath is not null;
            if ((loweredPath ?? Path(access.Expression, out textField)) is not { } textPath || textField?.Kind != DocKind.String)
                return Fail(invocation, $"{name} is called on a string field");
            var text = Value(arguments[0].Expression, textField);
            return text is null ? null : new DocCondition.Match(textPath, text, start: name == "StartsWith", end: name == "EndsWith", ignoreCase);
        }

        // Any on an embedded array (or an array of values): the element condition, or the array not empty.
        if (name == "Any" && method.ContainingType.ToDisplayString() == "System.Linq.Enumerable")
        {
            var receiver = method.IsExtensionMethod && method.ReducedFrom is not null ? access.Expression : arguments.Count > 0 ? arguments[0].Expression : access.Expression;
            if (Path(receiver, out var arrayField) is not { } arrayPath || arrayField?.Kind != DocKind.List)
                return Fail(invocation, "Any is called on a list field of the document");
            var predicate = method.ReducedFrom is not null ? arguments.FirstOrDefault()?.Expression : arguments.Skip(1).FirstOrDefault()?.Expression;
            if (predicate is null)
                return new DocCondition.NonEmpty(arrayPath);
            if (predicate is not SimpleLambdaExpressionSyntax { Body: ExpressionSyntax body } lambda || _model.GetDeclaredSymbol(lambda.Parameter) is not { } element)
                return Fail(invocation, "Any takes a lambda with an expression body");
            var elementModel = arrayField.Embedded is null ? null : _models(arrayField.Embedded);
            if (arrayField.Embedded is not null && elementModel is null)
                return Fail(invocation, $"{arrayField.Embedded.Name} is not a readable [Embedded] type");
            var inner = new DocTranslator(_model, elementModel, _models, element, _argument, _argumentCode, _diagnostics, _where, _treePaths, _captures);
            var condition = inner.Condition(body);
            Failed |= inner.Failed;
            return condition is null ? null : new DocCondition.ElemMatch(arrayPath, condition);
        }

        // A list of values' Contains with a field as the argument: $in.
        if (name == "Contains")
        {
            var (list, item) = method.ReducedFrom is not null || !method.IsStatic ? (access.Expression, arguments.FirstOrDefault()?.Expression) : (arguments.FirstOrDefault()?.Expression, arguments.Skip(1).FirstOrDefault()?.Expression);
            if (list is null || item is null || Path(item, out var itemField) is not { } itemPath || itemField is null)
                return Fail(invocation, "a list's Contains takes a field of the document: values.Contains(o.Field)");
            var values = Value(list, null);
            return values is null ? null : new DocCondition.In(itemPath, values, itemField.Kind == DocKind.Enum);
        }

        return Fail(invocation, $"{method.ContainingType.Name}.{name} is not translated: the calls are a string field's Contains, StartsWith, EndsWith, a list's Contains, an array's Any");
    }

    /// <summary>A member path on the lambda's parameter (<c>o.Address.City</c>): the stored path (<c>Address.City</c>, the key as <c>_id</c>), and the last field.</summary>
    public string? Path(ExpressionSyntax expression, out DocField? field)
    {
        field = null;
        if (expression is ParenthesizedExpressionSyntax paren)
            return Path(paren.Expression, out field);
        if (expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Value" } value && _model.GetTypeInfo(value.Expression).Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
            return Path(value.Expression, out field); // a nullable field's .Value: the field
        if (expression is IdentifierNameSyntax identifier && _root is null && SymbolEqualityComparer.Default.Equals(_model.GetSymbolInfo(identifier).Symbol, _parameter))
        {
            field = null;
            return ""; // the element itself, in an array of values
        }

        var segments = new List<string>();
        var current = Unsuppressed(expression);
        while (current is MemberAccessExpressionSyntax member)
        {
            segments.Insert(0, member.Name.Identifier.ValueText);
            current = Unsuppressed(member.Expression); // o.ShipTo!.Country: the '!' only silences the compiler
        }

        if (segments.Count == 0 || current is not IdentifierNameSyntax rootName || !SymbolEqualityComparer.Default.Equals(_model.GetSymbolInfo(rootName).Symbol, _parameter) || _root is null)
            return null;
        var model = _root;
        var names = new List<string>();
        for (var i = 0; i < segments.Count; i++)
        {
            var found = model?.Field(segments[i]);
            if (found is null)
                return null;
            names.Add(found.Name);
            field = found;
            if (i < segments.Count - 1)
            {
                if (found.Kind != DocKind.Embedded || found.Embedded is null)
                    return null;
                model = _models(found.Embedded);
            }
        }

        return string.Join(".", names);
    }

    /// <summary>
    ///     A value: a constant (written as a typed literal), null, or code over the two-parameter form's argument (<c>((T)s[k]!)</c>, its members and
    ///     their calls); converted to the field's kind (an enum as its Int32). A value of the enclosing method is PRAG3102.
    /// </summary>
    /// <summary>The value an ExecuteUpdateAsync's Set writes into <paramref name="field" />: as a comparison's value.</summary>
    public DocValue? SetValue(ExpressionSyntax expression, DocField field) => Value(expression, field);

    private DocValue? Value(ExpressionSyntax expression, DocField? field)
    {
        if (expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.NullLiteralExpression))
            return DocValue.Null;
        var constant = _model.GetConstantValue(expression);
        if (constant.HasValue)
        {
            if (constant.Value is null)
                return DocValue.Null;
            var text = Literal(constant.Value, field?.Kind == DocKind.Enum);
            return text is null ? Fail<DocValue>(expression, $"a constant of type {constant.Value.GetType().Name} is not a document value") : new DocValue(text);
        }

        string code;
        if (_captures is not null && !expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Any(id => SymbolEqualityComparer.Default.Equals(_model.GetSymbolInfo(id).Symbol, _parameter)))
        {
            // A specification's captured value: read off the lambda's tree when the specification is built, then a slot of its condition.
            if (_treePaths is null || !_treePaths.TryGetValue(expression, out var path))
                return Fail<DocValue>(expression, "a captured value of a specification is read where the compiler's tree has it: a variable, a member, or a call on them");
            var slot = _captures.Count;
            _captures.Add($"global::Pragmatic.Persistence.Documents.Pragmatic.Captured.Evaluate({path})");
            var valueType = _model.GetTypeInfo(expression).Type;
            code = valueType is null ? $"s[{slot}]" : $"(({valueType.ToDisplayString(DocModel.Qualified)})s[{slot}]!)";
        }
        else
        {
            CheckCaptures(expression);
            if (Failed)
                return null;
            if (_argument is null || _argumentCode is null)
                return Fail<DocValue>(expression, "the value is a constant, or the argument of the two-parameter form: Where((o, a) => o.Field == a, value)");
            var arguments = expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Where(id => SymbolEqualityComparer.Default.Equals(_model.GetSymbolInfo(id).Symbol, _argument)).ToList();
            if (arguments.Count == 0)
                return Fail<DocValue>(expression, "the value is a constant, or computed from the two-parameter form's argument");
            code = expression.ToString();
            var start = expression.SpanStart;
            foreach (var id in arguments.OrderByDescending(id => id.SpanStart))
                code = code.Substring(0, id.SpanStart - start) + _argumentCode + code.Substring(id.Span.End - start);
        }

        var type = _model.GetTypeInfo(expression);
        if (field?.Kind == DocKind.Enum)
            code = (type.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } ? "(int?)" : "(int)") + "(" + code + ")";
        else if (type.ConvertedType is { } converted && type.Type is { } actual && !SymbolEqualityComparer.Default.Equals(converted, actual) && converted.SpecialType != SpecialType.System_Object && DocModel.ScalarKind(Unwrap(converted)) is not null)
            code = "(" + converted.ToDisplayString(DocModel.Qualified) + ")(" + code + ")";
        return new DocValue(code);
    }

    /// <summary>PRAG3102 when the expression reads a local, a parameter or an instance member of the enclosing method (not the lambda's own parameters); PRAG3101 for a static member read at run time. Sets <see cref="Failed" />.</summary>
    private void CheckCaptures(ExpressionSyntax expression)
    {
        foreach (var identifier in expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            if (identifier.Parent is MemberAccessExpressionSyntax member && member.Name == identifier)
                continue; // a member's name, not a variable
            var symbol = _model.GetSymbolInfo(identifier).Symbol;
            if (SymbolEqualityComparer.Default.Equals(symbol, _parameter) || SymbolEqualityComparer.Default.Equals(symbol, _argument))
                continue;
            if (symbol is ILocalSymbol or IParameterSymbol || symbol is IFieldSymbol { IsStatic: false, IsConst: false } || symbol is IPropertySymbol { IsStatic: false })
            {
                if (!Failed)
                    _diagnostics.Add(Diagnostic.Create(DocDiagnostics.Captured, identifier.GetLocation(), identifier.Identifier.ValueText));
                Failed = true;
                return;
            }

            if (symbol is IFieldSymbol { IsStatic: true, IsConst: false } or IPropertySymbol { IsStatic: true } or IMethodSymbol { IsStatic: true })
            {
                Fail<DocValue>(expression, $"'{identifier.Identifier.ValueText}' is a static member read at run time: pass its value as the argument of the two-parameter form");
                return;
            }
        }
    }

    private static string? Literal(object value, bool asEnum)
    {
        switch (value)
        {
            case string text:
                return DocTypeEmitter.Literal(text);
            case bool flag:
                return flag ? "true" : "false";
            case int number:
                return number.ToString(CultureInfo.InvariantCulture);
            case long number:
                return number.ToString(CultureInfo.InvariantCulture) + "L";
            case decimal number:
                return number.ToString(CultureInfo.InvariantCulture) + "m";
            case double number when !double.IsNaN(number) && !double.IsInfinity(number):
                return number.ToString("R", CultureInfo.InvariantCulture) + "d";
            case float number when !float.IsNaN(number) && !float.IsInfinity(number):
                return ((double)number).ToString("R", CultureInfo.InvariantCulture) + "d";
            case byte or sbyte or short or ushort or char:
                return Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
            case uint number:
                return ((long)number).ToString(CultureInfo.InvariantCulture) + "L";
            default:
                return asEnum && value is IConvertible ? Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) : null;
        }
    }

    private static ExpressionSyntax Unsuppressed(ExpressionSyntax expression)
        => expression is PostfixUnaryExpressionSyntax postfix && postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression) ? postfix.Operand : expression;

    private static ITypeSymbol Unwrap(ITypeSymbol type) => type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } n ? n.TypeArguments[0] : type;

    private static string? Operator(SyntaxKind kind) => kind switch
    {
        SyntaxKind.EqualsExpression => "$eq",
        SyntaxKind.NotEqualsExpression => "$ne",
        SyntaxKind.LessThanExpression => "$lt",
        SyntaxKind.LessThanOrEqualExpression => "$lte",
        SyntaxKind.GreaterThanExpression => "$gt",
        SyntaxKind.GreaterThanOrEqualExpression => "$gte",
        _ => null,
    };

    private static string Flip(string op) => op switch { "$lt" => "$gt", "$lte" => "$gte", "$gt" => "$lt", "$gte" => "$lte", _ => op };

    private DocCondition? Fail(SyntaxNode node, string reason) => Fail<DocCondition>(node, reason);

    private T? Fail<T>(SyntaxNode node, string reason) where T : class
    {
        if (!Failed)
            _diagnostics.Add(Diagnostic.Create(DocDiagnostics.Unsupported, node.GetLocation() ?? _where, node.ToString(), reason));
        Failed = true;
        return null;
    }
}
