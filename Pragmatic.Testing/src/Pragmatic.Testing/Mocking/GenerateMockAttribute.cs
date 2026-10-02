namespace Pragmatic.Testing.Mocking;

/// <summary>
///     Declares that a mock implementation of <typeparamref name="T"/> should be generated into this
///     test assembly. Apply once per type, at assembly level:
///     <code>[assembly: GenerateMock&lt;IClock&gt;]</code>
/// </summary>
/// <remarks>
///     <para>
///         The generator emits a <c>{Type}Mock</c> class implementing <typeparamref name="T"/> with
///         explicit interface implementation, so each member is exposed twice: publicly under its own
///         name as a configurable <see cref="MockProperty{T}"/> or <c>MockMethod</c>, and explicitly
///         as the interface member the system under test calls.
///     </para>
///     <para>
///         Declaring the types rather than inferring them from usage is deliberate: a declaration is
///         greppable, and the generator stays on <c>ForAttributeWithMetadataName</c> instead of
///         scanning invocations, which keeps the incremental pipeline cheap.
///     </para>
/// </remarks>
/// <typeparam name="T">The interface to mock.</typeparam>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class GenerateMockAttribute<T> : Attribute
    where T : class
{
    /// <summary>
    ///     Overrides the generated class name. Defaults to the interface name without its leading
    ///     <c>I</c>, suffixed with <c>Mock</c> — <c>IClock</c> becomes <c>ClockMock</c>.
    /// </summary>
    public string? Name { get; set; }
}
