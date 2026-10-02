namespace Pragmatic.Testing.Assertions;

/// <summary>
///     Assertions for a value with no more specific family — the fallback <c>Should()</c> lands here.
/// </summary>
/// <typeparam name="TSubject">The type under test.</typeparam>
public sealed class ObjectAssertions<TSubject> : SubjectAssertions<TSubject, ObjectAssertions<TSubject>>
{
    internal ObjectAssertions(TSubject subject, string? expression) : base(subject, expression) { }

    // BeEquivalentTo is deliberately NOT a member here. It is generated, per type, from
    // [assembly: GenerateComparer<T>] — see Pragmatic.Testing.Comparers.SourceGenerator.
    //
    // A member would make that unreachable: an instance method always wins over an extension, so
    // the generated one would never be picked and the comparison would silently fall back to
    // Equals. Which is exactly what happened when both existed.
    //
    // Requiring the declaration is the point, as it is for mocks. Comparing two objects member by
    // member is a thing a suite should say it does, and on a type without value equality Equals
    // would compare references and report a difference that is not there.
}
