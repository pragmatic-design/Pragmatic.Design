namespace Pragmatic.Documents.Templating.Data;

/// <summary>
/// Marks a type parameter whose argument is data a template reads by property name.
/// </summary>
/// <remarks>
/// The source generator looks for calls and constructions that bind a type argument to a parameter
/// marked with this attribute, and writes property accessors for that type and for the types reached
/// from it, which <see cref="TemplateDataContext"/> uses instead of reflection. Every typed entry point
/// of the data context carries it: <see cref="TemplateDataContext.AddSource{T}"/>,
/// <see cref="DataSourceCatalog.Add{T}"/>, <see cref="StaticDataSource{T}"/> and the others.
/// </remarks>
[AttributeUsage(AttributeTargets.GenericParameter)]
public sealed class TemplateDataAttribute : Attribute;
