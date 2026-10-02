namespace Pragmatic.Privacy;

/// <summary>
///     The <see cref="ISubjectRegistry" /> an application gets when it has not chosen a store: it
///     answers nothing and says so.
/// </summary>
/// <remarks>
///     <para>
///         Classifying a field and referencing the runtime generates a personal-data source and an
///         erasure step per entity, and both take an <see cref="ISubjectRegistry" />. Registering the
///         registry is a separate decision — <c>AddSubjectRegistry()</c> needs a database, an encryptor
///         and a lookup key — and an application may legitimately want only the Article 30 register,
///         which needs none of them.
///     </para>
///     <para>
///         Without this the two cases were indistinguishable and both bad: under validation on build the
///         application refused to start with a dependency-injection dump, and without it it started and
///         failed at the first access request. Now the register works, and the parts that genuinely need
///         a store fail at the moment they are used, naming what to register.
///     </para>
///     <para>
///         <b>It throws rather than returning empty.</b> An access request answering "no data" and an
///         erasure reporting success are the two things a subject-rights process must never do when it
///         is simply not configured.
///     </para>
/// </remarks>
internal sealed class UnconfiguredSubjectRegistry : ISubjectRegistry
{
    private const string Explanation =
        "No ISubjectRegistry is registered. Subject access and erasure map a person to an opaque " +
        "reference through one, so neither can run without it. Call AddSubjectRegistry() from " +
        "Pragmatic.Privacy.EFCore — it also needs a PrivacyDbContext, an ISecretEncryptor and an " +
        "ISubjectLookupKeyProvider — or register your own implementation. The Article 30 register " +
        "does not use this and is unaffected.";

    public ValueTask<string> GetOrCreateReferenceAsync(
        string subjectType, string identifier, CancellationToken ct = default)
        => throw new InvalidOperationException(Explanation);

    public ValueTask<string?> FindReferenceAsync(
        string subjectType, string identifier, CancellationToken ct = default)
        => throw new InvalidOperationException(Explanation);

    public ValueTask<string?> ResolveIdentityAsync(string subjectRef, CancellationToken ct = default)
        => throw new InvalidOperationException(Explanation);

    public ValueTask<bool> ForgetAsync(string subjectRef, CancellationToken ct = default)
        => throw new InvalidOperationException(Explanation);
}
