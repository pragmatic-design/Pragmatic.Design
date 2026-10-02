using Pragmatic.SourceGenerator.Core;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Core;

/// <summary>
///     Pins how permission constants are named. Two templates render from this and the catalog is
///     contributed from it, so a change here that is not deliberate breaks
///     <c>[RequirePermission(GeneratedConstant)]</c> — silently, and open.
/// </summary>
public class PermissionNamingTests
{
    // ── Entity CRUD: the producer supplies the type name ──

    [Theory]
    [InlineData("catalog", "RoomType", "Read", "CatalogPermissions.RoomType.Read")]
    [InlineData("billing", "Invoice", "Create", "BillingPermissions.Invoice.Create")]
    [InlineData("catalog", "Property", "Resource", "CatalogPermissions.Property.Resource")]
    public void ForEntityMember_InsideABoundary_NestsUnderTheBoundaryClass(
        string slug, string typeName, string member, string expected)
        => PermissionNaming.ForEntityMember(slug, typeName, member).Should().Be(expected);

    [Fact]
    public void ForEntityMember_WithoutABoundary_IsTopLevel()
        => PermissionNaming.ForEntityMember(null, "Order", "Read").Should().Be("OrderPermissions.Read");

    /// <summary>
    ///     Kebab, on both segments. Entity CRUD, the view-all bypass and every trait spell the type name
    ///     the same way; flattening it in one of them makes one entity answer to two spellings — and on a
    ///     single-word name the two agree, which hides the split.
    /// </summary>
    [Theory]
    [InlineData("catalog", "RoomType", "read", "catalog.room-type.read")]
    [InlineData("catalog", "RoomType", null, "catalog.room-type")]
    [InlineData(null, "Order", "read", "order.read")]
    [InlineData("booking", "Guest", "read", "booking.guest.read")]
    [InlineData("frontdesk", "CaseFile", "read", "frontdesk.case-file.read")]
    public void ValueForEntityMember_KebabCasesBothSegments(
        string? slug, string typeName, string? verb, string expected)
        => PermissionNaming.ValueForEntityMember(slug, typeName, verb).Should().Be(expected);

    /// <summary>
    ///     The CRUD value and the view-all bypass name the same entity, so they must spell it the
    ///     same way.
    /// </summary>
    [Theory]
    [InlineData("RoomType")]
    [InlineData("GuestPreferences")]
    [InlineData("Guest")]
    public void CrudValueAndViewAllPermission_SpellTheEntityTheSameWay(string typeName)
    {
        var crud = PermissionNaming.ValueForEntityMember("booking", typeName, "read");
        var bypass = PermissionNaming.ViewAllPermission("booking", typeName);

        crud[..crud.LastIndexOf('.')].Should().Be(bypass[..bypass.LastIndexOf('.')]);
    }

    /// <summary>
    ///     The reason the catalog is contributed rather than re-derived. `roomtype` cannot become
    ///     `RoomType` again — no casing rule recovers a word boundary that lowercasing removed. If this
    ///     test ever passes, someone has taught `FromValue` a rule it cannot have, and entity permissions
    ///     will resolve against paths the template never emitted.
    /// </summary>
    [Fact]
    public void FromValue_CannotRecoverAnEntityTypeName_WhichIsWhyProducersContribute()
    {
        var emitted = PermissionNaming.ForEntityMember("catalog", "RoomType", "Read");
        var derived = PermissionNaming.FromValue("catalog.roomtype.read");

        emitted.Should().Be("CatalogPermissions.RoomType.Read");
        derived.Should().Be("CatalogPermissions.Roomtype.Read");
        derived.Should().NotBe(emitted);
    }

    // ── A declared permission ([assembly: Permission]): the value is the only source ──

    [Theory]
    [InlineData("billing.invoice.read", "BillingPermissions.Invoice.Read")]
    [InlineData("order.read", "OrderPermissions.Read")]
    [InlineData("billing.invoice.line.read", "BillingPermissions.Invoice.Line.Read")]
    public void FromValue_DottedValue_MirrorsTheEmittedPath(string value, string expected)
        => PermissionNaming.FromValue(value).Should().Be(expected);

    /// <summary>A bare category has no verb of its own, so it takes the Resource slot.</summary>
    [Fact]
    public void FromValue_BareCategory_UsesResource()
        => PermissionNaming.FromValue("billing").Should().Be("BillingPermissions.Resource");

    /// <summary>
    ///     The wildcard is the one segment that cannot become an identifier — the case the
    ///     correspondence is least likely to survive a refactor, so it is pinned at both depths.
    /// </summary>
    [Theory]
    [InlineData("billing.*", "BillingPermissions.All")]
    [InlineData("billing.invoice.*", "BillingPermissions.Invoice.All")]
    public void FromValue_Wildcard_UsesAll(string value, string expected)
        => PermissionNaming.FromValue(value).Should().Be(expected);

    [Theory]
    [InlineData("billing.invoice.read-all", "BillingPermissions.Invoice.ReadAll")]
    [InlineData("billing.invoice.read_all", "BillingPermissions.Invoice.ReadAll")]
    public void FromValue_KebabAndSnake_CollapseToOneIdentifier(string value, string expected)
        => PermissionNaming.FromValue(value).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromValue_NoValue_ReturnsNull(string? value)
        => PermissionNaming.FromValue(value).Should().BeNull();

    [Fact]
    public void ClassNameFor_AppendsTheSuffix()
        => PermissionNaming.ClassNameFor("billing").Should().Be("BillingPermissions");

    // Left whole, a derived permission is one flat segment — booking.addguestcomment — while a written
    // one has three: catalog.property.read. Splitting the leading verb gives both the same shape, which
    // is what a role file is read in.
    [Theory]
    [InlineData("AddGuestComment", "GuestComment", "add")]
    [InlineData("GetGuestComment", "GuestComment", "get")]
    [InlineData("RefundInvoice", "Invoice", "refund")]
    [InlineData("SetGuestPreferences", "GuestPreferences", "set")]
    public void TrySplitLeadingVerb_RecognisedVerb_SeparatesResourceFromVerb(
        string typeName, string expectedResource, string expectedVerb)
    {
        PermissionNaming.TrySplitLeadingVerb(typeName, out var resource, out var verb).Should().BeTrue();
        resource.Should().Be(expectedResource);
        verb.Should().Be(expectedVerb);
    }

    // Every rejection leaves the name exactly as it was, which is the guarantee that turning the switch
    // on cannot produce a name worse than the one it replaces.
    [Theory]
    // Not a verb at all. "Resource" opens 44 declared operations and is the trait generator's prefix.
    [InlineData("IssueRefund")]
    [InlineData("ResourceCreateGuest")]
    // A verb with nothing after it: there would be no resource to name.
    [InlineData("Create")]
    // The verb is only a prefix of a longer word — the next character does not start a new one.
    [InlineData("Setting")]
    [InlineData("Getter")]
    // The remainder reads as a phrase: splitting MarkAsRead would give the resource "asread".
    [InlineData("SetAsDefault")]
    [InlineData("RemoveFromBatch")]
    public void TrySplitLeadingVerb_NoUsefulSplit_LeavesTheNameWhole(string typeName)
    {
        PermissionNaming.TrySplitLeadingVerb(typeName, out var resource, out var verb).Should().BeFalse();
        resource.Should().Be(typeName);
        verb.Should().BeEmpty();
    }

    [Fact]
    public void ValueForEntityMember_WithAVerb_ProducesTheThreeSegmentShape()
        => PermissionNaming.ValueForEntityMember("booking", "GuestComment", "add")
            .Should().Be("booking.guest-comment.add");
}
