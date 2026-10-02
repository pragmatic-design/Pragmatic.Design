namespace Pragmatic.Documents.Templating.Data;

/// <summary>Marker interface for providers that hold a pre-resolved value.</summary>
internal interface IStaticDataSource
{
    object? GetValue();
}
