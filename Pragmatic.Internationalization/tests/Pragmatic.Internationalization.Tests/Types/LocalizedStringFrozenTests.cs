using Pragmatic.Testing.Assertions;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Tests.Types;

public class LocalizedStringFrozenTests
{
    [Fact]
    public void Empty_IsFrozen_SetThrows()
    {
        var act = () => LocalizedString.Empty.Set("en", "leak");

        act.Should().Throw<InvalidOperationException>();
        LocalizedString.Empty.IsEmpty.Should().BeTrue("the shared Empty singleton must stay empty");
    }

    [Fact]
    public void Empty_IsFrozen_SetCurrentThrows()
    {
        var act = () => LocalizedString.Empty.SetCurrent("leak");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Empty_IsFrozen_ClearThrows()
    {
        var act = () => LocalizedString.Empty.Clear();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Empty_IsFrozen_RemoveThrows()
    {
        var act = () => LocalizedString.Empty.Remove("en");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void NewInstance_IsMutable()
    {
        var s = new LocalizedString();

        s.Set("en", "Hello").Set("it", "Ciao");

        s.HasCulture("en").Should().BeTrue();
        s.HasCulture("it").Should().BeTrue();
    }

    [Fact]
    public void Mutation_DoesNotAffectPreviousReaderSnapshot()
    {
        // Copy-on-write: a reader that captured the dictionary before the mutation
        // must not observe the new entry (no torn state).
        var s = LocalizedString.From(("en", "Hello"));
        var snapshotBefore = s.Cultures.ToArray();

        s.Set("it", "Ciao");

        snapshotBefore.Should().BeEquivalentTo(["en"]);
        s.Cultures.Should().BeEquivalentTo(["en", "it"]);
    }
}
