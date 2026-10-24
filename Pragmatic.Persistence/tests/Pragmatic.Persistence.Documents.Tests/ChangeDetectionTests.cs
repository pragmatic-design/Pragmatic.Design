using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Documents.Mongo.Bson;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>The generated change detection (<see cref="IDocumentTracked{TSelf}" />), without a database: what a unit of work's save compares.</summary>
public sealed class ChangeDetectionTests
{
    [Fact]
    public void An_untouched_entity_has_no_change()
    {
        var stay = new Reservation { CheckIn = DateTimeOffset.UnixEpoch, Nights = 2 };
        Assert.Equal(0UL, Changed(stay, Snapshot(stay)));
    }

    [Fact]
    public void A_public_setter_and_a_generated_setter_each_mark_their_field()
    {
        var stay = new Reservation { Nights = 2 };
        var snapshot = Snapshot(stay);
        stay.Nights = 3;
        var nights = Changed(stay, snapshot);
        Assert.Equal(1UL, ulong.PopCount(nights));

        stay.Nights = 2;
        stay.SetOwnerId("u-9");
        var owner = Changed(stay, snapshot);
        Assert.Equal(1UL, ulong.PopCount(owner));
        Assert.NotEqual(nights, owner);
    }

    [Fact]
    public void A_change_inside_a_list_is_seen_and_written_alone()
    {
        var property = new Property { Name = "Riva", TenantId = "t1" };
        var snapshot = Snapshot(property);
        property.AccessScopes.Add("team:7");
        var mask = Changed(property, snapshot);
        Assert.Equal(1UL, ulong.PopCount(mask));

        var w = new BsonWriter();
        w.StartDocument();
        Write(w, property, mask);
        w.EndDocument();
        var set = new BsonDocument(w.Written.ToArray());
        Assert.Equal(["AccessScopes"], set.Names());
    }

    private static object?[] Snapshot<T>(T entity) where T : IDocumentTracked<T> => T.Snapshot(entity);

    private static ulong Changed<T>(T entity, object?[] snapshot) where T : IDocumentTracked<T> => T.Changed(entity, snapshot);

    private static void Write<T>(BsonWriter w, T entity, ulong mask) where T : IDocumentTracked<T> => T.WriteSet(w, entity, mask);
}
