using Pragmatic.SourceGen;

namespace Pragmatic.Result.SourceGenerator.Templates;

/// <summary>
///     Template for generating Result variants with multiple error types.
///     Generates Result&lt;TValue, TError1, TError2, ...&gt; types.
/// </summary>
internal sealed class ResultVariantTemplate : CSharpTemplate
{
    private readonly int _errorCount;
    private readonly string _errorTypeParams;
    private readonly string _typeParams;

    public ResultVariantTemplate(int errorCount)
    {
        _errorCount = errorCount;
        _typeParams = BuildTypeParams(true);
        _errorTypeParams = BuildTypeParams(false);
    }

    protected override string? GeneratorName => "Pragmatic.Result";

    public override Artifact RenderOutput()
    {
        var content = ToString();
        return Artifact.FromString($"Result{_errorCount + 1}_Generated.g.cs", content ?? "");
    }

    public override void RenderFile()
    {
        AddUsings("System", "System.Runtime.CompilerServices", "System.Diagnostics.CodeAnalysis");
        AppendNamespace("Pragmatic.Result");

        XmlSummary(
            $"Represents the result of an operation that can succeed with a value or fail with one of {_errorCount} error types.");
        AppendLine(
            "/// <remarks>Zero-allocation discriminated union with exhaustive pattern matching. Runtime validation ensures only declared error types can be used.</remarks>");

        // Generate struct with IError constraints
        var constraints = BuildGenericConstraints();
        AppendLine($"public readonly struct Result<{_typeParams}> : IResultBase");
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
        RenderMapAndBind();
        RenderImplicitOperators();
    }

    private void RenderFields()
    {
        Comment($"0 = success, 1-{_errorCount} = error types");
        Field("_index", "byte", AccessModifier.Private, true);
        Field("_value", "TValue?", AccessModifier.Private, true);

        for (var i = 1; i <= _errorCount; i++)
            Field($"_error{i}", $"TError{i}?", AccessModifier.Private, true);

        AppendLine();
    }

    private void RenderConstructors()
    {
        // Success constructor
        Constructor("Result", () =>
        {
            AppendLine("_index = 0;");
            AppendLine("_value = value;");
            for (var i = 1; i <= _errorCount; i++)
                AppendLine($"_error{i} = default;");
        }, [new MethodParameter("TValue", "value")], AccessModifier.Private);

        // Error constructors
        for (var i = 1; i <= _errorCount; i++)
            Constructor("Result", () =>
            {
                AppendLine($"_index = {i};");
                AppendLine("_value = default;");
                for (var j = 1; j <= _errorCount; j++)
                    AppendLine(j == i ? $"_error{j} = error;" : $"_error{j} = default;");
            }, [new MethodParameter($"TError{i}", "error")], AccessModifier.Private);
    }

    private void RenderProperties()
    {
        // IsSuccess - simple expression property (no attribute needed for these)
        ExpressionProperty("IsSuccess", "bool", "_index == 0");
        AppendLine();

        // IsFailure
        ExpressionProperty("IsFailure", "bool", "_index != 0");
        AppendLine();

        // Value property with getter
        PropertyWithGetter("Value", "TValue", () =>
        {
            AppendLine("[MethodImpl(MethodImplOptions.AggressiveInlining)]");
            AppendLine(
                "get => _index == 0 ? _value! : throw new InvalidOperationException(\"Cannot access Value when result is failure. Use TryGetValue() or Match().\");");
        });
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
        AppendLine("bool IResultBase.HasValueType => true;"); // Result<T> has a value type
        AppendLine("object? IResultBase.ValueAsObject => IsSuccess ? _value : null;");
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
        // TryGetValue
        Method("TryGetValue", () =>
            {
                AppendLine("value = _value;");
                AppendLine("return _index == 0;");
            }, "bool",
            [new MethodParameter("TValue", "value") { IsOut = true, Attribute = "MaybeNullWhen(false)" }],
            attribute: "MethodImpl(MethodImplOptions.AggressiveInlining)");

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
        Method("Success", () =>
            {
                AppendLine("ArgumentNullException.ThrowIfNull(value);");
                AppendLine($"return new Result<{_typeParams}>(value);");
            }, $"Result<{_typeParams}>",
            [new MethodParameter("TValue", "value")],
            modifiers: new MethodModifiers { IsStatic = true },
            attribute: "MethodImpl(MethodImplOptions.AggressiveInlining)");

        // Failure factories for each error type
        for (var i = 1; i <= _errorCount; i++)
            Method("Failure", () =>
                {
                    AppendLine("ArgumentNullException.ThrowIfNull(error);");
                    AppendLine($"return new Result<{_typeParams}>(error);");
                }, $"Result<{_typeParams}>",
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
                        AppendLine($"TError{i} e{i} => new Result<{_typeParams}>(e{i}),");
                    AppendLine("_ => throw new InvalidOperationException(\"Unexpected error type\")");
                });
                AppendLine(";");
            }, $"Result<{_typeParams}>",
            [new MethodParameter("IError", "error")],
            modifiers: new MethodModifiers { IsStatic = true });
    }

    private void RenderMatchMethods()
    {
        // Match with Func - generic method Match<TResult>
        var funcParams = new List<MethodParameter>
        {
            new("Func<TValue, TResult>", "onSuccess")
        };
        for (var i = 1; i <= _errorCount; i++)
            funcParams.Add(new MethodParameter($"Func<TError{i}, TResult>", $"onError{i}"));

        // A chain of tests, success first, rather than a switch on the index: with three cases or more the JIT
        // compiles the switch to a jump table, one indirect jump per call, where the chain is direct branches.
        // MultiError_Match against its hand-written twin (#132) is the row that decides it.
        GenericMethod("Match", ["TResult"], () =>
            {
                AppendLine("if (_index == 0)");
                AppendLine("    return onSuccess(_value!);");
                for (var i = 1; i <= _errorCount; i++)
                {
                    AppendLine($"if (_index == {i})");
                    AppendLine($"    return onError{i}(_error{i}!);");
                }

                AppendLine("throw new InvalidOperationException(\"Invalid result state\");");
            }, "TResult", funcParams,
            attribute: "MethodImpl(MethodImplOptions.AggressiveInlining)");

        // Match with Action
        var actionParams = new List<MethodParameter>
        {
            new("Action<TValue>", "onSuccess")
        };
        for (var i = 1; i <= _errorCount; i++)
            actionParams.Add(new MethodParameter($"Action<TError{i}>", $"onError{i}"));

        Method("Match", () =>
        {
            AppendLine("switch (_index)");
            Block(() =>
            {
                AppendLine("case 0: onSuccess(_value!); break;");
                for (var i = 1; i <= _errorCount; i++)
                    AppendLine($"case {i}: onError{i}(_error{i}!); break;");
                AppendLine("default: throw new InvalidOperationException(\"Invalid result state\");");
            });
        }, "void", actionParams);

        // Match with generic error handler - for when you don't need to handle each error type separately
        AppendLine();
        Comment(
            "Matches the result with a generic error handler. Use when you don't need to handle each error type separately.");
        GenericMethod("Match", ["TResult"],
            () => { AppendLine("return IsSuccess ? onSuccess(_value!) : onError(Error);"); }, "TResult",
            [
                new MethodParameter("Func<TValue, TResult>", "onSuccess"),
                new MethodParameter("Func<IError, TResult>", "onError")
            ],
            attribute: "MethodImpl(MethodImplOptions.AggressiveInlining)");
    }

    private void RenderMapAndBind()
    {
        // Map<TNewValue>
        GenericMethod("Map", ["TNewValue"], () =>
            {
                AppendLine("return _index switch");
                Block(() =>
                {
                    AppendLine($"0 => Result<TNewValue, {_errorTypeParams}>.Success(mapper(_value!)),");
                    for (var i = 1; i <= _errorCount; i++)
                        AppendLine($"{i} => Result<TNewValue, {_errorTypeParams}>.Failure(_error{i}!),");
                    AppendLine("_ => throw new InvalidOperationException(\"Invalid result state\")");
                });
                AppendLine(";");
            }, $"Result<TNewValue, {_errorTypeParams}>",
            [new MethodParameter("Func<TValue, TNewValue>", "mapper")],
            attribute: "MethodImpl(MethodImplOptions.AggressiveInlining)");

        // Bind<TNewValue>
        GenericMethod("Bind", ["TNewValue"], () =>
            {
                AppendLine("return _index switch");
                Block(() =>
                {
                    AppendLine("0 => binder(_value!),");
                    for (var i = 1; i <= _errorCount; i++)
                        AppendLine($"{i} => Result<TNewValue, {_errorTypeParams}>.Failure(_error{i}!),");
                    AppendLine("_ => throw new InvalidOperationException(\"Invalid result state\")");
                });
                AppendLine(";");
            }, $"Result<TNewValue, {_errorTypeParams}>",
            [new MethodParameter($"Func<TValue, Result<TNewValue, {_errorTypeParams}>>", "binder")],
            attribute: "MethodImpl(MethodImplOptions.AggressiveInlining)");
    }

    private void RenderImplicitOperators()
    {
        // Value to Result
        ImplicitOperator("TValue value", $"Result<{_typeParams}>", "Success(value)",
            "MethodImpl(MethodImplOptions.AggressiveInlining)");
        AppendLine();

        // Each error type to Result
        for (var i = 1; i <= _errorCount; i++)
        {
            ImplicitOperator($"TError{i} error", $"Result<{_typeParams}>", "Failure(error)",
                "MethodImpl(MethodImplOptions.AggressiveInlining)");
            AppendLine();
        }

        // Result to Value (throws if failure)
        Comment("Implicitly converts a result to its value. Throws if the result is a failure.");
        AppendLine("[MethodImpl(MethodImplOptions.AggressiveInlining)]");
        AppendLine($"public static implicit operator TValue(Result<{_typeParams}> result) => result.Value;");
    }

    private string BuildTypeParams(bool includeValue)
    {
        var parts = new List<string>();
        if (includeValue)
            parts.Add("TValue");
        for (var i = 1; i <= _errorCount; i++)
            parts.Add($"TError{i}");
        return string.Join(", ", parts);
    }
}