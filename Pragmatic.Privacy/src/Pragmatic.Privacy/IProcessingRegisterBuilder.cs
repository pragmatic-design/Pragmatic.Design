namespace Pragmatic.Privacy;

/// <summary>
///     Produces the Article 30 register as the system stands.
/// </summary>
/// <remarks>
///     The interface exists so an operation can depend on the register: the generator does not inject a
///     concrete type, because it cannot tell an injected service from plain state (<c>PRAG0419</c>).
///     Implemented by <see cref="ProcessingRegisterBuilder" />.
/// </remarks>
public interface IProcessingRegisterBuilder
{
    /// <inheritdoc cref="ProcessingRegisterBuilder.BuildAsync" />
    ValueTask<ProcessingRegister> BuildAsync(CancellationToken ct = default);
}
