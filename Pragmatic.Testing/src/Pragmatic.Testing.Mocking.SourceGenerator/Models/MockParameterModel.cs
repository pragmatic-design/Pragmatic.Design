namespace Pragmatic.Testing.Mocking.SourceGenerator.Models;

/// <summary>One parameter of a mocked method: its fully-qualified type and its name.</summary>
/// <param name="Type">The fully-qualified parameter type, <c>global::</c>-prefixed.</param>
/// <param name="Name">The parameter name, reused verbatim in the generated signature.</param>
/// <param name="Modifier">
///     <c>ref </c>, <c>out </c> or <c>in </c>, with its trailing space; empty for a by-value
///     parameter. It is part of the signature, and dropping it silently produces a member that
///     overrides nothing — Azure's <c>GenerateSasUri(BlobSasBuilder, out string)</c> found this.
/// </param>
internal sealed record MockParameterModel(string Type, string Name, string Modifier = "");
