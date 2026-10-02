using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Result.Models;

namespace Pragmatic.SourceGenerator.Features.Result.Templates;

/// <summary>
///     A JSON converter for one closed multi-error result type.
/// </summary>
/// <remarks>
///     <para>
///         Replaces <c>MultiErrorResultJsonConverter&lt;TResult&gt;</c>, which found the value type with
///         <c>GetGenericArguments()[0]</c>, the factories with <c>GetMethod("Success"/"Failure")</c>, and
///         called them through <c>MethodInfo.Invoke</c>. All three are compile-time facts about a closed
///         type, so the generated converter simply names them.
///     </para>
///     <para>
///         Reading never has to infer anything: the payload carries <c>isSuccess</c>, so value and error
///         are told apart by the wire, and the error's own <c>$errorType</c> selects one of the declared
///         slots. Nothing here asks the runtime what a type is.
///     </para>
/// </remarks>
internal sealed class MultiErrorResultConverterTemplate : CSharpTemplate
{
    private const string Json = "global::System.Text.Json";
    private const string ErrorJson = "global::Pragmatic.Result.Serialization.ErrorJson";
    private const string TypeInfo = "global::Pragmatic.Serialization.PragmaticJsonTypeInfo";

    private readonly ResultContractModel _model;
    private readonly string _namespace;

    public MultiErrorResultConverterTemplate(ResultContractModel model, string generatedNamespace)
    {
        _model = model;
        _namespace = generatedNamespace;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Result";
    protected override string? SourceInfo => Display;
    protected override string? TriggerInfo => $"[JsonResultContract<{Display}>]";

    /// <summary>The result type without global::, for text a human reads.</summary>
    private string Display => _model.ResultTypeName.Replace("global::", "");

    protected override bool Validate() => _model.IsGenerated && _model.ErrorSlots.Length > 0;

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_model.ConverterName, "ResultConverter", _namespace), ToSourceText());

    public override void RenderFile()
    {
        AppendNamespace(_namespace);
        AppendLine();

        XmlSummary($"JSON converter for {Display}.");
        Class(_model.ConverterName, RenderBody,
            baseType: $"{Json}.Serialization.JsonConverter<{_model.ResultTypeName}>",
            accessModifier: AccessModifier.Internal,
            modifiers: new ClassModifiers { Sealed = true });
    }

    private void RenderBody()
    {
        RenderRead();
        AppendLine();
        RenderWrite();
    }

    private void RenderRead()
    {
        AppendLine("/// <inheritdoc />");
        AppendLine($"public override {_model.ResultTypeName} Read(ref {Json}.Utf8JsonReader reader,");
        AppendLine($"    global::System.Type typeToConvert, {Json}.JsonSerializerOptions options)");
        Block(() =>
        {
            AppendLine($"if (reader.TokenType != {Json}.JsonTokenType.StartObject)");
            AppendLine($"    throw new {Json}.JsonException(\"Expected StartObject token\");");
            AppendLine();
            AppendLine($"using var document = {Json}.JsonDocument.ParseValue(ref reader);");
            AppendLine();
            var hasValue = _model.ValueTypeName is not null;

            AppendLine("var isSuccess = default(bool?);");
            if (hasValue)
                AppendLine($"var valueElement = default({Json}.JsonElement);");
            AppendLine($"var errorElement = default({Json}.JsonElement);");
            if (hasValue)
                AppendLine("var hasValue = false;");
            AppendLine("var hasError = false;");
            AppendLine();
            // Case-insensitive, as the hand-written converter was: the payload may come from a producer
            // whose naming policy is not ours.
            AppendLine("foreach (var property in document.RootElement.EnumerateObject())");
            Block(() =>
            {
                AppendLine("if (string.Equals(property.Name, \"isSuccess\", global::System.StringComparison.OrdinalIgnoreCase))");
                AppendLine("    isSuccess = property.Value.GetBoolean();");
                if (hasValue)
                {
                    AppendLine("else if (string.Equals(property.Name, \"value\", global::System.StringComparison.OrdinalIgnoreCase))");
                    Block(() =>
                    {
                        AppendLine("valueElement = property.Value;");
                        AppendLine("hasValue = true;");
                    });
                }
                AppendLine("else if (string.Equals(property.Name, \"error\", global::System.StringComparison.OrdinalIgnoreCase))");
                Block(() =>
                {
                    AppendLine("errorElement = property.Value;");
                    AppendLine("hasError = true;");
                });
            });
            AppendLine();
            AppendLine("if (!isSuccess.HasValue)");
            AppendLine($"    throw new {Json}.JsonException(\"Missing 'isSuccess' property\");");
            AppendLine();
            AppendLine("if (isSuccess.Value)");
            Block(RenderReadSuccess);
            AppendLine();
            AppendLine("if (!hasError)");
            AppendLine($"    throw new {Json}.JsonException(\"Missing 'error' property for failure result\");");
            AppendLine();
            AppendLine($"var discriminator = {ErrorJson}.ReadDiscriminator(errorElement);");
            AppendLine();
            AppendLine("switch (discriminator)");
            Block(() =>
            {
                foreach (var slot in _model.ErrorSlots)
                {
                    AppendLine($"case \"{StringHelper.CSharpLiteral(slot.Discriminator)}\":");
                    AppendLine($"    return {_model.ResultTypeName}.Failure(");
                    AppendLine($"        {ErrorJson}.Read(errorElement, {TypeInfo}.For<{slot.TypeName}>(options)));");
                }

                AppendLine("default:");
                AppendLine($"    throw new {Json}.JsonException(");
                AppendLine($"        $\"Error type '{{discriminator}}' is not one of the declared error types of \"");
                AppendLine($"        + \"{StringHelper.CSharpLiteral(Display)}.\");");
            });
        });
    }

    private void RenderReadSuccess()
    {
        if (_model.ValueTypeName is null)
        {
            AppendLine($"return {_model.ResultTypeName}.Success();");
            return;
        }

        AppendLine("if (!hasValue)");
        AppendLine($"    throw new {Json}.JsonException(\"Missing 'value' property for success result\");");
        AppendLine();
        AppendLine($"var value = {Json}.JsonSerializer.Deserialize(valueElement,");
        AppendLine($"    {TypeInfo}.For<{_model.ValueTypeName}>(options));");
        AppendLine();

        // `is null` against a non-nullable value type does not compile (CS0037), and the check would
        // mean nothing there anyway: Deserialize<int> cannot hand back null.
        if (_model.ValueCanBeNull)
        {
            AppendLine("if (value is null)");
            AppendLine($"    throw new {Json}.JsonException(");
            AppendLine($"        \"A success '{StringHelper.CSharpLiteral(Display)}' requires a non-null 'value'.\");");
            AppendLine();
        }

        AppendLine($"return {_model.ResultTypeName}.Success(value);");
    }

    private void RenderWrite()
    {
        AppendLine("/// <inheritdoc />");
        AppendLine($"public override void Write({Json}.Utf8JsonWriter writer,");
        AppendLine($"    {_model.ResultTypeName} value, {Json}.JsonSerializerOptions options)");
        Block(() =>
        {
            AppendLine("writer.WriteStartObject();");
            AppendLine("writer.WriteBoolean(\"isSuccess\", value.IsSuccess);");
            AppendLine();
            AppendLine("if (value.IsSuccess)");
            Block(RenderWriteSuccess);
            AppendLine("else");
            Block(RenderWriteError);
            AppendLine();
            AppendLine("writer.WriteEndObject();");
        });
    }

    private void RenderWriteSuccess()
    {
        if (_model.ValueTypeName is null)
        {
            AppendLine("// A void result carries nothing on success.");
            return;
        }

        AppendLine("writer.WritePropertyName(\"value\");");
        AppendLine($"{Json}.JsonSerializer.Serialize(writer, value.Value,");
        AppendLine($"    {TypeInfo}.For<{_model.ValueTypeName}>(options));");
    }

    private void RenderWriteError()
    {
        AppendLine("writer.WritePropertyName(\"error\");");
        AppendLine();
        AppendLine("switch (value.Error)");
        Block(() =>
        {
            foreach (var slot in _model.ErrorSlots)
            {
                AppendLine($"case {slot.TypeName} error:");
                AppendLine($"    {ErrorJson}.Write(writer, error,");
                AppendLine($"        {TypeInfo}.For<{slot.TypeName}>(options),");
                AppendLine($"        \"{StringHelper.CSharpLiteral(slot.Discriminator)}\");");
                AppendLine("    break;");
            }

            // Failure(IError) accepts any IError, so a result can hold an error outside its slots.
            AppendLine("default:");
            AppendLine($"    throw new {Json}.JsonException(");
            AppendLine($"        $\"Error type '{{value.Error.GetType()}}' is not one of the declared error types of \"");
            AppendLine($"        + \"{StringHelper.CSharpLiteral(Display)}.\");");
        });
    }
}
