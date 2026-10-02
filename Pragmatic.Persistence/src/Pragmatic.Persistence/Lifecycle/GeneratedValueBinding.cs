using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Persistence.Lifecycle;

/// <summary>
///     The generated-code side of <see cref="IGeneratedValueBinding" />: reads, tests and writes one
///     property through delegates the source generator supplies.
/// </summary>
/// <typeparam name="TEntity">The entity that owns the property.</typeparam>
/// <typeparam name="TValue">The property's type.</typeparam>
/// <remarks>
///     Delegates rather than reflection, because the generator knows the property at compile time and
///     a <c>PropertyInfo.SetValue</c> here would put reflection on the write path of every save. The
///     setter is a delegate and not a property assignment so a private setter reached through the
///     generated <c>Set{Property}</c> method works the same as a public one.
/// </remarks>
public sealed class GeneratedValueBinding<TEntity, TValue> : IGeneratedValueBinding
    where TEntity : class
{
    private readonly IDefaultValueGenerator<TEntity, TValue> _generator;
    private readonly Func<TEntity, TValue> _read;
    private readonly Action<TEntity, TValue> _write;
    private readonly Func<TValue, bool> _isEmpty;

    /// <summary>Creates a binding for one generated property.</summary>
    /// <param name="propertyName">The property's name, used in diagnostics.</param>
    /// <param name="generator">The generator that produces the value.</param>
    /// <param name="read">Reads the current value.</param>
    /// <param name="write">Writes the generated value.</param>
    /// <param name="isEmpty">Decides whether the current value counts as absent.</param>
    public GeneratedValueBinding(
        string propertyName,
        IDefaultValueGenerator<TEntity, TValue> generator,
        Func<TEntity, TValue> read,
        Action<TEntity, TValue> write,
        Func<TValue, bool> isEmpty)
    {
        PropertyName = ThrowIfNullOrWhiteSpace(propertyName);
        _generator = ThrowIfNull(generator);
        _read = ThrowIfNull(read);
        _write = ThrowIfNull(write);
        _isEmpty = ThrowIfNull(isEmpty);
    }

    /// <inheritdoc />
    public Type EntityType => typeof(TEntity);

    /// <inheritdoc />
    public string PropertyName { get; }

    /// <inheritdoc />
    public async Task<bool> TryFillAsync(object entity, LifecycleContext context, CancellationToken ct)
    {
        if (entity is not TEntity typed || !_isEmpty(_read(typed)))
            return false;

        _write(typed, await _generator.GenerateAsync(typed, context, ct).ConfigureAwait(false));
        return true;
    }
}
