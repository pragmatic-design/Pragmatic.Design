using Pragmatic.SourceGen;

namespace Pragmatic.Result.SourceGenerator.Templates;

/// <summary>
///     Template for generating VoidResult variants with multiple error types.
///     Generates VoidResult&lt;TError1, TError2, ...&gt; types.
/// </summary>
internal sealed class VoidResultVariantTemplate : CSharpTemplate
{
    private readonly int _errorCount;
    private readonly string _typeParams;

    public VoidResultVariantTemplate(int errorCount)
    {
        _errorCount = errorCount;
        _typeParams = BuildTypeParams();
    }

    protected override string? GeneratorName => "Pragmatic.Result";

    public override Artifact RenderOutput()
    {
        var content = ToString();
        return Artifact.FromString($"VoidResult{_errorCount}_Generated.g.cs", content ?? "");
    }

    public override void RenderFile()
    {
        AddUsings("System", "System.Runtime.CompilerServices", "System.Diagnostics.CodeAnalysis");
        AppendNamespace("Pragmatic.Result");

        XmlSummary($"Represents the result of a void operation that can fail with one of {_errorCount} error types.");
        AppendLine(
            "/// <remarks>Zero-allocation discriminated union with exhaustive pattern matching. Runtime validation ensures only declared error types can be used.</remarks>");

        // Generate struct with IError constraints
        var constraints = BuildGenericConstraints();
        AppendLine($"public readonly struct VoidResult<{_typeParams}> : IResultBase");
        foreach (var constraint in constraints)
            AppendLine($"    {constraint}");
        Block(RenderBody);
        AppendLine();
    }

    private List<string> BuildGenericConstraints()
    {
        var constraints = new List<string>();
        for (var i = 1; i <= _errorCount; i++)
            constraints.Add($"where TError{i} : IError");
        return constraints;
    }

    private void RenderBody()
    {
        RenderFields();
        RenderConstructors();
        RenderProperties();
        RenderIResultBaseImplementation();
        RenderTryGetMethods();
        RenderFactoryMethods();
        RenderMatchMethods();
        RenderImplicitOperators();
    }

    private void RenderFields()
    {
        Comment($"0 = success, 1-{_errorCount} = error types");
        Field("_index", "byte", AccessModifier.Private, true);

        for (var i = 1; i <= _errorCount; i++)
            Field($"_error{i}", $"TError{i}?", AccessModifier.Private, true);

        AppendLine();
    }

    private void RenderConstructors()
    {
        // Success constructor (uses bool parameter to distinguish)
        Constructor("VoidResult", () =>
        {
            AppendLine("_index = 0;");
            for (var i = 1; i <= _errorCount; i++)
                AppendLine($"_error{i} = default;");
        }, [new MethodParameter("bool", "_")], AccessModifier.Private);

        // Error constructors
        for (var i = 1; i <= _errorCount; i++)
            Constructor("VoidResult", () =>
            {
                AppendLine($"_index = {i};");
                for (var j = 1; j <= _errorCount; j++)
                    AppendLine(j == i ? $"_error{j} = error;" : $"_error{j} = default;");
            }, [new MethodParameter($"TError{i}", "error")], AccessModifier.Private);
    }

    private void RenderProperties()
    {
        // IsSuccess - simple expression property
        ExpressionProperty("IsSuccess", "bool", "_index == 0");
        AppendLine();

        // IsFailure
        ExpressionProperty("IsFailure", "bool", "_index != 0");
        AppendLine();

        // Error properties for each type
        for (var i = 1; i <= _errorCount; i++)
        {
            PropertyWithGetter($"Error{i}", $"TError{i}", () =>
            {
                AppendLine("[MethodImpl(MethodImplOptions.AggressiveInlining)]");
                AppendLine(
                    $"get => _index == {i} ? _error{i}! : throw new InvalidOperationException(\"Cannot access Error{i} when result is not Error{i} type. Use TryGetError{i}() or Match().\");");
            });
            AppendLine();
        }

        // Generic Error property returning IError
        Comment("Gets the error as the base IError type.");
        PropertyWithGetter("Error", "IError", () =>
        {
            AppendLine("[MethodImpl(MethodImplOptions.AggressiveInlining)]");
            AppendLine(
                "get => IsSuccess ? throw new InvalidOperationException(\"Cannot access Error when result is success.\") : GetFirstError()!;");
        });
        AppendLine();
    }

    private void RenderIResultBaseImplementation()
    {
        Comment("IResultBase explicit implementation");
        AppendLine("bool IResultBase.IsSuccess => IsSuccess;");
        AppendLine("bool IResultBase.IsFailure => IsFailure;");
        AppendLine("bool IResultBase.HasValueType => false;"); // VoidResult has no value type
        AppendLine("object? IResultBase.ValueAsObject => null;"); // VoidResult has no value
        AppendLine("IError? IResultBase.ErrorAsObject => IsSuccess ? null : GetFirstError();");
        AppendLine();

        // GetFirstError helper method.
        // Only ever invoked when the result is a failure (_index in 1.._errorCount);
        // _index == 0 (success) is filtered out by callers, so the default arm is unreachable.
        Method("GetFirstError", () =>
            {
                AppendLine("return _index switch");
                AppendLine("{");
                IncreaseIndent();
                for (var i = 1; i <= _errorCount; i++)
                    AppendLine($"{i} => _error{i},");
                AppendLine("_ => throw new InvalidOperationException(\"Unreachable: GetFirstError() called on a success result.\")");
                DecreaseIndent();
                AppendLine("};");
            }, "IError?", [],
            AccessModifier.Private,
            attribute: "MethodImpl(MethodImplOptions.AggressiveInlining)");
    }

    private void RenderTryGetMethods()
    {
        // TryGetError for each error type
        for (var i = 1; i <= _errorCount; i++)
            Method($"TryGetError{i}", () =>
                {
                    AppendLine($"error = _error{i};");
                    AppendLine($"return _index == {i};");
                }, "bool",
                [new MethodParameter($"TError{i}", "error") { IsOut = true, Attribute = "MaybeNullWhen(false)" }],
                attribute: "MethodImpl(MethodImplOptions.AggressiveInlining)");
    }

    private void RenderFactoryMethods()
    {
        // Success factory
        ExpressionMethod("Success", $"new VoidResult<{_typeParams}>(true)",
            $"VoidResult<{_typeParams}>",
            modifiers: new MethodModifiers { IsStatic = true },
            attribute: "MethodImpl(MethodImplOptions.AggressiveInlining)");
        AppendLine();

        // Failure factories for each error type
        for (var i = 1; i <= _errorCount; i++)
            Method("Failure", () =>
                {
                    AppendLine("ArgumentNullException.ThrowIfNull(error);");
                    AppendLine($"return new VoidResult<{_typeParams}>(error);");
                }, $"VoidResult<{_typeParams}>",
                [new MethodParameter($"TError{i}", "error")],
                modifiers: new MethodModifiers { IsStatic = true },
                attribute: "MethodImpl(MethodImplOptions.AggressiveInlining)");

        // Failure factory from generic Error with runtime validation
        AppendLine();
        Comment("Creates a failed result from a generic Error. Validates that the error is one of the declared types.");
        Method("Failure", () =>
            {
                AppendLine("ArgumentNullException.ThrowIfNull(error);");
                var typeChecks = string.Join(" and ", Enumerable.Range(1, _errorCount).Select(i => $"not TError{i}"));
                AppendLine($"if (error is {typeChecks})");
                Block(() =>
                {
                    AppendLine("throw new ArgumentException(");
                    AppendLine("    $\"Error must be one of the declared types, but was {error.GetType().Name}\",");
                    AppendLine("    nameof(error));");
                });
                // Cast to first matching type and create
                AppendLine("return error switch");
                Block(() =>
                {
                    for (var i = 1; i <= _errorCount; i++)
                        AppendLine($"TError{i} e{i} => new VoidResult<{_typeParams}>(e{i}),");
                    AppendLine("_ => throw new InvalidOperationException(\"Unexpected error type\")");
                });
                AppendLine(";");
            }, $"VoidResult<{_typeParams}>",
            [new MethodParameter("IError", "error")],
            modifiers: new MethodModifiers { IsStatic = true });
    }

    private void RenderMatchMethods()
    {
        // Match with Func - generic method Match<TResult>
        var funcParams = new List<MethodParameter>
        {
            new("Func<TResult>", "onSuccess")
        };
        for (var i = 1; i <= _errorCount; i++)
            funcParams.Add(new MethodParameter($"Func<TError{i}, TResult>", $"onError{i}"));

        GenericMethod("Match", ["TResult"], () =>
            {
                AppendLine("return _index switch");
                Block(() =>
                {
                    AppendLine("0 => onSuccess(),");
                    for (var i = 1; i <= _errorCount; i++)
                        AppendLine($"{i} => onError{i}(_error{i}!),");
                    AppendLine("_ => throw new InvalidOperationException(\"Invalid result state\")");
                });
                AppendLine(";");
            }, "TResult", funcParams,
            attribute: "MethodImpl(MethodImplOptions.AggressiveInlining)");

        // Match with Action
        var actionParams = new List<MethodParameter>
        {
            new("Action", "onSuccess")
        };
        for (var i = 1; i <= _errorCount; i++)
            actionParams.Add(new MethodParameter($"Action<TError{i}>", $"onError{i}"));

        Method("Match", () =>
        {
            AppendLine("switch (_index)");
            Block(() =>
            {
                AppendLine("case 0: onSuccess(); break;");
                for (var i = 1; i <= _errorCount; i++)
                    AppendLine($"case {i}: onError{i}(_error{i}!); break;");
                AppendLine("default: throw new InvalidOperationException(\"Invalid result state\");");
            });
        }, "void", actionParams);

        // Match with generic error handler - for when you don't need to handle each error type separately
        AppendLine();
        Comment(
            "Matches the result with a generic error handler. Use when you don't need to handle each error type separately.");
        GenericMethod("Match", ["TResult"], () => { AppendLine("return IsSuccess ? onSuccess() : onError(Error);"); },
            "TResult",
            [
                new MethodParameter("Func<TResult>", "onSuccess"),
                new MethodParameter("Func<IError, TResult>", "onError")
            ],
            attribute: "MethodImpl(MethodImplOptions.AggressiveInlining)");
    }

    private void RenderImplicitOperators()
    {
        // Each error type to VoidResult
        for (var i = 1; i <= _errorCount; i++)
        {
            ImplicitOperator($"TError{i} error", $"VoidResult<{_typeParams}>", "Failure(error)",
                "MethodImpl(MethodImplOptions.AggressiveInlining)");
            AppendLine();
        }

        // VoidResult to bool
        ImplicitOperator($"VoidResult<{_typeParams}> result", "bool", "result.IsSuccess",
            "MethodImpl(MethodImplOptions.AggressiveInlining)");
    }

    private string BuildTypeParams()
    {
        var parts = new List<string>();
        for (var i = 1; i <= _errorCount; i++)
            parts.Add($"TError{i}");
        return string.Join(", ", parts);
    }
}