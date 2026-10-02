using Pragmatic.Mapping.EFCore.Tests.Dtos;

namespace Pragmatic.Mapping.EFCore.Tests;

/// <summary>
///     How deep does an update preserve the identity of the children it writes?
/// </summary>
/// <remarks>
///     <para>
///         The first level is settled: <c>MapOneToMany</c> matches by key, so updating an order's
///         lines keeps their rows. For the level below — the lines of the orders of a user — the
///         answer is not obvious from the code: Mapping recurses when it computes required
///         navigations, and the mutation-child model does not.
///     </para>
///     <para>
///         The answer, measured rather than assumed: it does. The generated write passes
///         <c>(d, e) =&gt; d.ApplyTo(e, context)</c> as the updater of <c>MapOneToMany</c>, so each child
///         merges its own children in turn — recursion by construction, to whatever depth the DTOs
///         declare. What is flat is <c>MutationChildModel</c>, which is the other road to the same
///         write: a mutation with children rather than a tree of DTOs.
///     </para>
///     <para>
///         ⚠️ Nothing else pins this down. A "before" read off the tracked entity is read after
///         <c>ApplyTo</c> has mutated it: a measurement taken after the write measures nothing, and
///         its failure looks like a framework defect.
///     </para>
/// </remarks>
public class NestedDepthTests : PostgresTestBase
{
    /// <summary>The first level, as a control: updating an order keeps the order's row.</summary>
    /// <remarks>
    ///     Here so the deeper test can be read. If this one failed too, the answer would be about
    ///     <c>ApplyTo</c> in general and not about depth.
    /// </remarks>
    [Fact]
    public async Task FirstLevel_UpdatingAnOrder_KeepsItsIdentity()
    {
        var user = await Db.Users
            .Include(u => u.Orders).ThenInclude(o => o.Lines)
            .FirstAsync(u => u.Email == "john.doe@example.com");

        var before = user.Orders.OrderBy(o => o.OrderNumber)
            .Select(o => (o.Id, o.OrderNumber)).ToList();
        var totalsBefore = user.Orders.ToDictionary(o => o.Id, o => o.Total);

        var dto = new UserWriteDto
        {
            Id = user.Id,
            Email = user.Email,
            Orders = [.. user.Orders.OrderBy(o => o.OrderNumber).Select(o => new OrderWriteDto
            {
                Id = o.Id,
                OrderNumber = o.OrderNumber,
                Total = o.Total + 1m,
                Lines = [.. o.Lines.Select(l => new OrderLineWriteDto
                {
                    Id = l.Id,
                    ProductName = l.ProductName,
                    Quantity = l.Quantity,
                    UnitPrice = l.UnitPrice
                })]
            })]
        };

        dto.ApplyTo(user, Db);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var reloaded = await Db.Users
            .Include(u => u.Orders).ThenInclude(o => o.Lines)
            .FirstAsync(u => u.Email == "john.doe@example.com");

        reloaded.Orders.OrderBy(o => o.OrderNumber).Select(o => (o.Id, o.OrderNumber))
            .Should().Equal(before, "an order matched by key is updated, not replaced");

        // And the control: the write reached the first level at all.
        var firstBefore = before[0];
        reloaded.Orders.Single(o => o.Id == firstBefore.Id).Total
            .Should().Be(totalsBefore[firstBefore.Id] + 1m,
                "the value one level down has to have been written");
    }

    /// <summary>
    ///     ⚠️ The measurement: at the second level, do the lines keep their rows?
    /// </summary>
    /// <remarks>
    ///     The control is inside the test rather than beside it: the changed value has to have been
    ///     written. Without that half, a generated <c>ApplyTo</c> that silently skipped the nested
    ///     collection would pass — the identities would be unchanged for the worst possible reason.
    /// </remarks>
    [Fact]
    public async Task SecondLevel_UpdatingTheLinesOfAnOrder_KeepsTheirIdentity()
    {
        var user = await Db.Users
            .Include(u => u.Orders).ThenInclude(o => o.Lines)
            .FirstAsync(u => u.Email == "john.doe@example.com");

        var order = user.Orders.OrderBy(o => o.OrderNumber).First(o => o.Lines.Count > 0);
        var lineIdsBefore = order.Lines.OrderBy(l => l.ProductName).Select(l => l.Id).ToList();
        var targetProduct = order.Lines.OrderBy(l => l.ProductName).First().ProductName;

        // ⚠️ Captured before ApplyTo: `order` is the tracked entity, and ApplyTo mutates it. Reading
        // it afterwards would compare the result with itself, and the failure would look like a
        // framework defect.
        var quantityBefore = order.Lines.Single(l => l.ProductName == targetProduct).Quantity;

        var dto = new UserWriteDto
        {
            Id = user.Id,
            Email = user.Email,
            Orders = [.. user.Orders.OrderBy(o => o.OrderNumber).Select(o => new OrderWriteDto
            {
                Id = o.Id,
                OrderNumber = o.OrderNumber,
                Total = o.Total,
                Lines = [.. o.Lines.OrderBy(l => l.ProductName).Select(l => new OrderLineWriteDto
                {
                    Id = l.Id,
                    ProductName = l.ProductName,
                    // the one change, two levels down
                    Quantity = l.ProductName == targetProduct ? l.Quantity + 7 : l.Quantity,
                    UnitPrice = l.UnitPrice
                })]
            })]
        };

        dto.ApplyTo(user, Db);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var reloaded = await Db.Users
            .Include(u => u.Orders).ThenInclude(o => o.Lines)
            .FirstAsync(u => u.Email == "john.doe@example.com");

        var reloadedOrder = reloaded.Orders.Single(o => o.Id == order.Id);

        // The control: the write reached two levels down. Assert it first — if this fails, the
        // identity assertion below is measuring nothing.
        reloadedOrder.Lines.Single(l => l.ProductName == targetProduct).Quantity
            .Should().Be(quantityBefore + 7,
                "the value two levels down has to have been written, or the test below is vacuous");

        reloadedOrder.Lines.OrderBy(l => l.ProductName).Select(l => l.Id)
            .Should().Equal(lineIdsBefore,
                "a line matched by key should be updated rather than discarded and rebuilt");
    }

    /// <summary>An element removed from the nested DTO disappears from the database too.</summary>
    /// <remarks>
    ///     <c>Sync</c> means "this is the complete list": what does not arrive goes away. The test above
    ///     measures only the updating branch, and a merge that ignored absences would pass it.
    /// </remarks>
    [Fact]
    public async Task SecondLevel_RemovingALine_RemovesTheRow()
    {
        var user = await Db.Users
            .Include(u => u.Orders).ThenInclude(o => o.Lines)
            .FirstAsync(u => u.Email == "john.doe@example.com");

        var order = user.Orders.OrderBy(o => o.OrderNumber).First(o => o.Lines.Count > 1);
        var keep = order.Lines.OrderBy(l => l.ProductName).First();
        var drop = order.Lines.OrderBy(l => l.ProductName).Last();

        var dto = Shape(user, order.Id, lines: [Line(keep)]);

        dto.ApplyTo(user, Db);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var reloaded = await Db.Orders.Include(o => o.Lines).FirstAsync(o => o.Id == order.Id);

        reloaded.Lines.Select(l => l.Id).Should().Equal([keep.Id],
            "the line the DTO stopped carrying is the one Sync removes");
        (await Db.OrderLines.AnyAsync(l => l.Id == drop.Id)).Should().BeFalse(
            "and the row is gone, not merely detached");
    }

    /// <summary>A new element in the nested DTO is created with its foreign key.</summary>
    /// <remarks>
    ///     ⚠️ The piece this measures and the rest does not touch: the FK to the parent. A child created
    ///     two levels down is attached to the right parent's collection, or EF refuses it — and without
    ///     a test the difference is found in production.
    /// </remarks>
    [Fact]
    public async Task SecondLevel_AddingALine_CreatesItUnderTheRightOrder()
    {
        var user = await Db.Users
            .Include(u => u.Orders).ThenInclude(o => o.Lines)
            .FirstAsync(u => u.Email == "john.doe@example.com");

        var order = user.Orders.OrderBy(o => o.OrderNumber).First(o => o.Lines.Count > 0);
        var existing = order.Lines.OrderBy(l => l.ProductName).Select(Line).ToList();
        existing.Add(new OrderLineWriteDto
        {
            Id = 0, ProductName = "Widget Z", Quantity = 3, UnitPrice = 9.99m
        });

        var dto = Shape(user, order.Id, [.. existing]);

        dto.ApplyTo(user, Db);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var reloaded = await Db.Orders.Include(o => o.Lines).FirstAsync(o => o.Id == order.Id);
        var added = reloaded.Lines.Single(l => l.ProductName == "Widget Z");

        added.OrderId.Should().Be(order.Id, "a child created two levels down belongs to its parent");
        added.Quantity.Should().Be(3);
    }

    /// <summary>
    ///     ⚠️ An <b>unloaded</b> nested collection duplicates the rows, and this test pins it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         With <c>Sync</c> and a collection never included, <c>entity.Lines</c> is empty: every
    ///         element of the DTO looks new, and an <c>ApplyToLoaded</c> that sends back the same two
    ///         lines writes four. No error, <c>SaveChanges</c> succeeds. Of the three possible outcomes
    ///         — duplicates, a foreign key violation, a silent deletion — it is duplicates.
    ///     </para>
    ///     <para>
    ///         <b>Where the framework prevents it</b>: on a mutation's path, whose invoker loads what
    ///         the children write — their navigation, and the paths the children's DTOs declare in
    ///         turn — and through the <c>ApplyTo(entity, context)</c> overload, which asks EF and
    ///         refuses. This test calls <c>ApplyToLoaded</c> by hand, where nobody can know what the
    ///         caller loaded: <c>MapOneToMany</c> receives an <c>ICollection</c>, and in EF an empty
    ///         collection and an unloaded one are the same thing.
    ///     </para>
    ///     <para>
    ///         The test therefore asserts the real behaviour, not the desirable one, because a green
    ///         test on a guarantee that does not exist is worse than no test.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task SecondLevel_WhenTheLinesWereNeverLoaded_TheyAreDuplicated()
    {
        var seeded = await Db.Users
            .Include(u => u.Orders).ThenInclude(o => o.Lines)
            .FirstAsync(u => u.Email == "john.doe@example.com");
        var orderId = seeded.Orders.OrderBy(o => o.OrderNumber).First(o => o.Lines.Count > 0).Id;
        var lineCount = seeded.Orders.Single(o => o.Id == orderId).Lines.Count;
        var lines = seeded.Orders.Single(o => o.Id == orderId).Lines
            .OrderBy(l => l.ProductName).Select(Line).ToList();
        var originalIds = seeded.Orders.Single(o => o.Id == orderId).Lines.Select(l => l.Id).ToList();
        Db.ChangeTracker.Clear();

        // The parent without ThenInclude: the orders are there, their lines are not.
        var user = await Db.Users.Include(u => u.Orders)
            .FirstAsync(u => u.Email == "john.doe@example.com");

        var dto = Shape(user, orderId, [.. lines]);

        dto.ApplyToLoaded(user);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var reloaded = await Db.Orders.Include(o => o.Lines).FirstAsync(o => o.Id == orderId);

        // The fact, not the count: the original rows are still there <b>and</b> others with the same
        // content have appeared. Asserting only the double would let through a deletion followed by
        // a re-creation, which is a different defect worth telling apart.
        reloaded.Lines.Select(l => l.Id).Should().Contain(originalIds,
            "the rows that were there are still there — nothing was removed");

        reloaded.Lines.Count.Should().Be(lineCount * 2,
            "and every line the DTO carried was written again, because an unloaded collection reads as "
            + "an empty one and makes each of them look new. This is the behaviour, not the intent: a "
            + "mutation avoids it by loading what its children write, and a caller with a context can "
            + "use the overload that refuses");

        reloaded.Lines.GroupBy(l => l.ProductName).Should().OnlyContain(g => g.Count() == 2,
            "the duplicates are of the same lines, which is what identifies this as a failed match "
            + "rather than a write of something else");
    }

    // ── helpers ──

    private static OrderLineWriteDto Line(Entities.OrderLine l) => new()
    {
        Id = l.Id, ProductName = l.ProductName, Quantity = l.Quantity, UnitPrice = l.UnitPrice
    };

    /// <summary>The root DTO, with the given lines for the named order and the existing ones for the others.</summary>
    private static UserWriteDto Shape(Entities.User user, int orderId, List<OrderLineWriteDto> lines) => new()
    {
        Id = user.Id,
        Email = user.Email,
        Orders = [.. user.Orders.OrderBy(o => o.OrderNumber).Select(o => new OrderWriteDto
        {
            Id = o.Id,
            OrderNumber = o.OrderNumber,
            Total = o.Total,
            Lines = o.Id == orderId ? lines : [.. o.Lines.OrderBy(l => l.ProductName).Select(Line)]
        })]
    };

    /// <summary>Creating the whole tree at once: every node is new, and the foreign keys go down.</summary>
    /// <remarks>
    ///     Quite different from an update: there is nothing to match against, so the question is not
    ///     whether identity survives but whether the rows are created <b>attached to the right parent</b>.
    ///     A wrong level here gives orphan rows or a foreign key violation.
    /// </remarks>
    [Fact]
    public async Task Creating_TheWholeTree_AttachesEveryLevel()
    {
        var dto = new UserWriteDto
        {
            Email = "tree@example.com",
            Orders =
            [
                new OrderWriteDto
                {
                    OrderNumber = "ORD-TREE",
                    Total = 12.34m,
                    Lines =
                    [
                        new OrderLineWriteDto { ProductName = "Deep A", Quantity = 1, UnitPrice = 5m },
                        new OrderLineWriteDto { ProductName = "Deep B", Quantity = 2, UnitPrice = 3.67m }
                    ]
                }
            ]
        };

        var user = dto.ToEntity();
        Db.Users.Add(user);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var reloaded = await Db.Users
            .Include(u => u.Orders).ThenInclude(o => o.Lines)
            .FirstAsync(u => u.Email == "tree@example.com");

        var order = reloaded.Orders.Single();
        order.OrderNumber.Should().Be("ORD-TREE");
        order.UserId.Should().Be(reloaded.Id, "the order belongs to the user that created it");

        order.Lines.Select(l => l.ProductName).OrderBy(n => n)
            .Should().Equal("Deep A", "Deep B");
        order.Lines.Should().OnlyContain(l => l.OrderId == order.Id,
            "and every line two levels down belongs to its order");
    }

    /// <summary>
    ///     ⚠️ <c>Sync</c> removes at the first level what the DTO does not carry, and keeps the lines of what it does.
    /// </summary>
    /// <remarks>
    ///     The control that separates "the merge goes down" from "the merge goes down and behaves the
    ///     same way". An order absent from the DTO disappears, and its lines with it: if the lines of a
    ///     <i>present</i> order disappeared too, the merge would be rebuilding instead of merging.
    /// </remarks>
    [Fact]
    public async Task Sync_RemovesAnAbsentOrder_AndKeepsTheLinesOfThePresentOne()
    {
        var user = await Db.Users
            .Include(u => u.Orders).ThenInclude(o => o.Lines)
            .FirstAsync(u => u.Email == "john.doe@example.com");

        var keep = user.Orders.OrderBy(o => o.OrderNumber).First(o => o.Lines.Count > 0);
        var drop = user.Orders.OrderBy(o => o.OrderNumber).First(o => o.Id != keep.Id);
        var keptLineIds = keep.Lines.OrderBy(l => l.ProductName).Select(l => l.Id).ToList();

        var dto = new UserWriteDto
        {
            Id = user.Id,
            Email = user.Email,
            Orders =
            [
                new OrderWriteDto
                {
                    Id = keep.Id,
                    OrderNumber = keep.OrderNumber,
                    Total = keep.Total,
                    Lines = [.. keep.Lines.OrderBy(l => l.ProductName).Select(Line)]
                }
            ]
        };

        dto.ApplyTo(user, Db);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var reloaded = await Db.Users
            .Include(u => u.Orders).ThenInclude(o => o.Lines)
            .FirstAsync(u => u.Id == user.Id);

        reloaded.Orders.Select(o => o.Id).Should().Equal([keep.Id],
            "the order the DTO stopped carrying is removed");
        reloaded.Orders.Single().Lines.OrderBy(l => l.ProductName).Select(l => l.Id)
            .Should().Equal(keptLineIds,
                "and the lines of the order that stayed are the same rows, not rebuilt ones");
        (await Db.Orders.AnyAsync(o => o.Id == drop.Id)).Should().BeFalse();
    }

    /// <summary>
    ///     ⚠️ <c>AddOnly</c> declared on a <b>nested</b> collection really applies down there.
    /// </summary>
    /// <remarks>
    ///     Calling the helper directly at the first level is a different path: here the declaration
    ///     sits on a DTO the merge of the level above invokes as an updater, and the generated template
    ///     must carry it down. A template that lost the attribute on the way down would use
    ///     <c>Sync</c> — and the line the DTO does not carry would disappear.
    /// </remarks>
    [Fact]
    public async Task SecondLevel_AddOnly_KeepsTheLineTheDtoDoesNotCarry()
    {
        var user = await Db.Users
            .Include(u => u.Orders).ThenInclude(o => o.Lines)
            .FirstAsync(u => u.Email == "john.doe@example.com");

        var order = user.Orders.OrderBy(o => o.OrderNumber).First(o => o.Lines.Count > 1);
        var keep = order.Lines.OrderBy(l => l.ProductName).First();
        var omitted = order.Lines.OrderBy(l => l.ProductName).Last();

        var dto = new UserAddOnlyDto
        {
            Id = user.Id,
            Email = user.Email,
            Orders = [.. user.Orders.OrderBy(o => o.OrderNumber).Select(o => new OrderAddOnlyLinesDto
            {
                Id = o.Id,
                OrderNumber = o.OrderNumber,
                Total = o.Total,
                // only one line for the order under test: under Sync the other would disappear
                Lines = o.Id == order.Id
                    ? [Line(keep)]
                    : [.. o.Lines.OrderBy(l => l.ProductName).Select(Line)]
            })]
        };

        dto.ApplyTo(user, Db);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var reloaded = await Db.Orders.Include(o => o.Lines).FirstAsync(o => o.Id == order.Id);

        reloaded.Lines.Select(l => l.Id).Should().Contain(omitted.Id,
            "AddOnly does not remove what the DTO stopped carrying, two levels down as at one");
        reloaded.Lines.Count.Should().Be(order.Lines.Count,
            "and nothing was added either — the DTO carried no new element");
    }

    /// <summary>An existing single navigation is updated, not re-created.</summary>
    /// <remarks>
    ///     ⚠️ The <c>MapOneToOne</c> branch, through a generated DTO. The same question as for
    ///     collections applies: does the row survive or is it replaced.
    /// </remarks>
    [Fact]
    public async Task SingleNavigation_UpdatingIt_KeepsTheRow()
    {
        var user = await Db.Users.Include(u => u.Address)
            .FirstAsync(u => u.Email == "john.doe@example.com" && u.Address != null);

        var addressId = user.Address!.Id;

        var dto = new UserAddressWriteDto
        {
            Id = user.Id,
            Email = user.Email,
            Address = new AddressWriteDto
            {
                Id = addressId,
                Street = "Via Nuova 1",
                City = user.Address.City,
                Country = user.Address.Country,
                PostalCode = user.Address.PostalCode
            }
        };

        dto.ApplyTo(user, Db);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var reloaded = await Db.Users.Include(u => u.Address).FirstAsync(u => u.Id == user.Id);

        reloaded.Address!.Id.Should().Be(addressId, "the row is updated, not replaced");
        reloaded.Address.Street.Should().Be("Via Nuova 1", "and the change was written");
    }

    /// <summary>
    ///     ⚠️ A <c>null</c> on a single navigation <b>leaves it alone</b>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A missing field has two possible readings — «remove it» and «I am not telling you about
    ///         this» — and a null alone cannot tell them apart. <c>MapOneToOne</c> takes the second, the
    ///         same partial-update reading the scalars use, because the first is the destructive one.
    ///     </para>
    ///     <para>
    ///         The test pins the behaviour so that it stays visible: if references ever get modes of
    ///         their own, this turns red and that is the signal.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task SingleNavigation_ANullInTheDto_LeavesItAlone()
    {
        var user = await Db.Users.Include(u => u.Address)
            .FirstAsync(u => u.Email == "john.doe@example.com" && u.Address != null);

        var dto = new UserAddressWriteDto { Id = user.Id, Email = user.Email, Address = null };

        dto.ApplyTo(user, Db);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var reloaded = await Db.Users.Include(u => u.Address).FirstAsync(u => u.Id == user.Id);

        reloaded.Address.Should().NotBeNull(
            "a null reference is read as \"I am not telling you about this\", the same partial-update "
            + "reading the scalars use — not as \"remove it\", which is the destructive half of an "
            + "ambiguity nobody can resolve from a null alone");
    }

    /// <summary>
    ///     ⚠️ The measure that decides whether the "unloaded collection" case is detectable.
    /// </summary>
    /// <remarks>
    ///     <c>MapOneToMany</c> receives an <c>ICollection</c> and cannot tell empty from unloaded. EF
    ///     can, and the question is whether it can with the precision needed: an included collection
    ///     and one never touched must give different answers on the <b>same</b> entity, or there is
    ///     nothing to build a guard on.
    /// </remarks>
    [Fact]
    public async Task Probe_EfKnowsWhetherACollectionWasLoaded()
    {
        var withLines = await Db.Users
            .Include(u => u.Orders).ThenInclude(o => o.Lines)
            .FirstAsync(u => u.Email == "john.doe@example.com");
        var loadedOrder = withLines.Orders.First(o => o.Lines.Count > 0);
        var loaded = Db.Entry(loadedOrder).Collection(nameof(Entities.Order.Lines)).IsLoaded;

        Db.ChangeTracker.Clear();

        var withoutLines = await Db.Users.Include(u => u.Orders)
            .FirstAsync(u => u.Email == "john.doe@example.com");
        var bareOrder = withoutLines.Orders.First(o => o.Id == loadedOrder.Id);
        var notLoaded = Db.Entry(bareOrder).Collection(nameof(Entities.Order.Lines)).IsLoaded;

        loaded.Should().BeTrue("the collection came back with the query");
        notLoaded.Should().BeFalse(
            "and one that was never included says so — which is the fact a guard can be built on, and "
            + "the one MapOneToMany cannot see from an ICollection alone");

        bareOrder.Lines.Should().BeEmpty(
            "meanwhile the collection itself looks exactly like an empty one, which is why the merge "
            + "treats every incoming element as new");
    }

    /// <summary>
    ///     ⚠️ The overload that asks EF refuses instead of duplicating.
    /// </summary>
    /// <remarks>
    ///     The same write that, with <c>ApplyToLoaded(entity)</c>, silently doubles the rows. Passing
    ///     the context, the answer is an exception naming the missing navigation.
    ///     ⚠️ It fires <em>at</em> the navigation, not before the write: the tracked entity has already
    ///     been touched when it throws. Nothing is persisted because <c>SaveChanges</c> is not called —
    ///     it is the caller who must not save after seeing it.
    /// </remarks>
    [Fact]
    public async Task ApplyToWithContext_RefusesAnUnloadedCollection()
    {
        var seeded = await Db.Users
            .Include(u => u.Orders).ThenInclude(o => o.Lines)
            .FirstAsync(u => u.Email == "john.doe@example.com");
        var orderId = seeded.Orders.OrderBy(o => o.OrderNumber).First(o => o.Lines.Count > 0).Id;
        var lines = seeded.Orders.Single(o => o.Id == orderId).Lines
            .OrderBy(l => l.ProductName).Select(Line).ToList();
        Db.ChangeTracker.Clear();

        var user = await Db.Users.Include(u => u.Orders)
            .FirstAsync(u => u.Email == "john.doe@example.com");

        var dto = Shape(user, orderId, [.. lines]);

        var act = () => dto.ApplyTo(user, Db);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Order.Lines*",
                "the refusal names the navigation that was not loaded — Lines on Order, not Orders "
                + "on User, which was included and is fine. The check happens at the write, so it "
                + "names what the write was about to touch");
    }

    /// <summary>The control: with the collection loaded, the overload writes like the other one.</summary>
    /// <remarks>
    ///     Without this, a guard that always refused would still pass the test above.
    /// </remarks>
    [Fact]
    public async Task ApplyToWithContext_WithEverythingLoaded_WritesAsUsual()
    {
        var user = await Db.Users
            .Include(u => u.Orders).ThenInclude(o => o.Lines)
            .FirstAsync(u => u.Email == "john.doe@example.com");

        var order = user.Orders.OrderBy(o => o.OrderNumber).First(o => o.Lines.Count > 0);
        var lineIdsBefore = order.Lines.OrderBy(l => l.ProductName).Select(l => l.Id).ToList();

        var dto = Shape(user, order.Id, [.. order.Lines.OrderBy(l => l.ProductName).Select(Line)]);

        dto.ApplyTo(user, Db);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var reloaded = await Db.Orders.Include(o => o.Lines).FirstAsync(o => o.Id == order.Id);

        reloaded.Lines.OrderBy(l => l.ProductName).Select(l => l.Id)
            .Should().Equal(lineIdsBefore, "loaded, so the merge matches by key and keeps the rows");
    }

    /// <summary>
    ///     ⚠️ The same write with <c>AddOnly</c>: the unloaded collection still duplicates.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <c>AddOnly</c> may look like a remedy next to the include, because it does not read the
    ///     collection to decide what to remove. But it still reads it to decide what is <em>new</em>,
    ///     and duplicates identically. This test is why it is not recommended for that.
    /// </remarks>
    [Fact]
    public async Task AddOnly_WithAnUnloadedCollection_StillDuplicates()
    {
        var seeded = await Db.Users
            .Include(u => u.Orders).ThenInclude(o => o.Lines)
            .FirstAsync(u => u.Email == "john.doe@example.com");
        var orderId = seeded.Orders.OrderBy(o => o.OrderNumber).First(o => o.Lines.Count > 0).Id;
        var lineCount = seeded.Orders.Single(o => o.Id == orderId).Lines.Count;
        var lines = seeded.Orders.Single(o => o.Id == orderId).Lines
            .OrderBy(l => l.ProductName).Select(Line).ToList();
        Db.ChangeTracker.Clear();

        var user = await Db.Users.Include(u => u.Orders)
            .FirstAsync(u => u.Email == "john.doe@example.com");

        var dto = new UserAddOnlyDto
        {
            Id = user.Id,
            Email = user.Email,
            Orders = [.. user.Orders.OrderBy(o => o.OrderNumber).Select(o => new OrderAddOnlyLinesDto
            {
                Id = o.Id,
                OrderNumber = o.OrderNumber,
                Total = o.Total,
                Lines = o.Id == orderId ? [.. lines] : []
            })]
        };

        dto.ApplyToLoaded(user);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var reloaded = await Db.Orders.Include(o => o.Lines).FirstAsync(o => o.Id == orderId);

        reloaded.Lines.Count.Should().Be(lineCount * 2,
            "AddOnly does not remove, but it still reads the collection to decide what is new — so an "
            + "unloaded one makes every element look new all the same. The advice to reach for it "
            + "protects against losing rows, not against writing them twice");
    }

    /// <summary>
    ///     ⚠️ The mirror defect, on a single navigation: unloaded looks absent.
    /// </summary>
    /// <remarks>
    ///     An unloaded collection writes the same rows twice. An unloaded <em>reference</em> fails the
    ///     other way: <c>MapOneToOne</c> reads <c>null</c>, concludes the child is not there, and builds
    ///     a second one next to the one in the database. Here the one-to-one has a unique index on
    ///     <c>UserId</c>, so the write breaks instead of silently duplicating — but the cause the
    ///     database reports is not the real one, which is why refusing earlier is worth it.
    /// </remarks>
    [Fact]
    public async Task SingleNavigation_WhenNotLoaded_BuildsASecondChild()
    {
        // The tracker is cleared first: seeding leaves the Address tracked too, and EF attaches the
        // navigation by fixup — without this, IsLoaded is true without any Include and the test
        // measures a state no real query produces.
        Db.ChangeTracker.Clear();

        // No Include(u => u.Address): the reference stays unloaded.
        var user = await Db.Users.FirstAsync(u => u.Email == "john.doe@example.com");

        var dto = new UserAddressWriteDto
        {
            Id = user.Id,
            Email = user.Email,
            Address = new AddressWriteDto
            {
                Street = "Via Nuova 1",
                City = "Milano",
                Country = "IT",
                PostalCode = "20100"
            }
        };

        dto.ApplyToLoaded(user);

        var act = () => Db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>(
            "the reference looked absent, so the merge built a second Address for a user that "
            + "already has one. It is never the in-place update the caller asked for: here the "
            + "one-to-one unique index refuses it, and elsewhere it would simply be a second row");
    }

    /// <summary>The refusal applies to a single navigation too, not only to collections.</summary>
    /// <remarks>
    ///     The tracked form goes through the <c>ReferenceEntry</c>, which knows <c>IsLoaded</c> like the
    ///     <c>CollectionEntry</c>. Without this test the reference branch of <c>EfMutationHelpers</c>
    ///     would be written and never exercised.
    /// </remarks>
    [Fact]
    public async Task SingleNavigation_WhenNotLoaded_IsRefused()
    {
        Db.ChangeTracker.Clear();

        var user = await Db.Users.FirstAsync(u => u.Email == "john.doe@example.com");

        var dto = new UserAddressWriteDto
        {
            Id = user.Id,
            Email = user.Email,
            Address = new AddressWriteDto
            {
                Street = "Via Nuova 1",
                City = "Milano",
                Country = "IT",
                PostalCode = "20100"
            }
        };

        var act = () => dto.ApplyTo(user, Db);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*User.Address*",
                "the refusal names the reference that was not loaded, before anything is written");
    }
}
