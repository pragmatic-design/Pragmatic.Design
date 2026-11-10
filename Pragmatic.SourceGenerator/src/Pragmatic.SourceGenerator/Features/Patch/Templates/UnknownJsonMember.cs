namespace Pragmatic.SourceGenerator.Features.Patch.Templates;

/// <summary>
///     How a generated patch converter passes over a member the patch does not declare: the lines both patch converters emit
///     (<see cref="PatchConverterTemplate" /> and the persistence one), so the two cannot read a body differently.
/// </summary>
/// <remarks>
///     <para>
///         <c>reader.Skip()</c> throws on a reader that is not on the final block, and that is the reader a converter is handed when
///         ASP.NET Core reads the request body through a <c>PipeReader</c>, as it does on .NET 10: a body naming a member the patch did
///         not declare was a 400 "could not be converted".
///     </para>
///     <para>
///         <c>TrySkip</c> succeeds there, because the serializer buffers a value whole before it calls a converter. Answering false would
///         mean it did not, so that is refused by name rather than read past by hand.
///     </para>
/// </remarks>
internal static class UnknownJsonMember
{
    /// <summary>The condition under which the member could not be passed over.</summary>
    public const string CouldNotSkip = "!reader.TrySkip()";

    /// <summary>The refusal for that case: a 400 naming what happened, not an <c>InvalidOperationException</c>.</summary>
    public const string Refusal =
        "throw new global::System.Text.Json.JsonException($\"The value of '{propertyName}', which this patch does not declare, is not complete in the buffer.\");";
}
