using System.Text.Json.Serialization;

namespace PragmaticDocumentSamples.Queries;

/// <summary>A grid's filter read as a request body is (its values JsonElements), without reflection: Native AOT.</summary>
[JsonSerializable(typeof(List<object?>))]
internal sealed partial class SampleJson : JsonSerializerContext;
