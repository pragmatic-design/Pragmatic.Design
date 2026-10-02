namespace Pragmatic.Persistence.Query.Attributes;

/// <summary>
///     Marks a query property as a complex filter DTO.
///     The value is deserialized from a JSON query string parameter via <see cref="Converters.JsonQueryConverter{T}" />.
///     The property type must be decorated with <see cref="FilterDtoAttribute{TEntity}" />.
/// </summary>
/// <remarks>
///     The source generator will call <c>{PropertyType}Extensions.ApplyFilter(query, this.{Property})</c>
///     inside the generated <c>Apply()</c> method.
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ComplexFilterAttribute : Attribute;
