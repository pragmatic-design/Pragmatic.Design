using Pragmatic.Persistence.Sql;

namespace Pragmatic.Persistence.Workload;

// Entities for the model features the benchmark workload does not need (types, converters, owned types, a hierarchy in
// one table, a tree, a JSON column). Their tables come from ExtrasSchema, created after the workload's and left empty:
// the tests and the native smoke write and read them; the benchmarks and the harness never touch them.

/// <summary>Every column type beyond the workload's, and two converted columns: the stamp as UTC ticks, the status as its name.</summary>
[Entity]
[Table("TypedRows")]
public sealed class TypedRow : ISqlEntity
{
    public Guid Id { get; set; }
    public DateTime When { get; set; }
    public byte[] Blob { get; set; } = [];
    public DateOnly Day { get; set; }
    public TimeOnly Clock { get; set; }
    public TimeSpan Span { get; set; }

    [Convert<SqlUtcTicks>]
    public DateTimeOffset Stamp { get; set; }

    [Convert<SqlEnumName<OrderStatus>>]
    public OrderStatus Status { get; set; }

    public DateTime? MaybeWhen { get; set; }

    [Convert<SqlUtcTicks>]
    public DateTimeOffset? MaybeStamp { get; set; }
}

/// <summary>The stamp of a typed row, read through the converted column into a DTO: the declared projection yields the model type.</summary>
[MapFrom<TypedRow>]
public sealed class TypedStampDto
{
    public Guid Id { get; set; }
    public DateTimeOffset Stamp { get; set; }
    public OrderStatus Status { get; set; }
}

/// <summary>An owned type: no table of its own, its members flattened into the owner's as <c>{Property}_{Member}</c>.</summary>
[Owned]
public sealed class Address
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public int Floor { get; set; }
}

/// <summary>A row with a required owned type and an optional one (null when its columns are).</summary>
[Entity]
[Table("Shipments")]
public sealed class Shipment : ISqlEntity
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public Address Destination { get; set; } = new();
    public Address? Return { get; set; }
}

/// <summary>A declared projection reaching into the owned type: by path, and by the <c>DestinationCity</c> convention.</summary>
[MapFrom<Shipment>]
public sealed class ShipmentRowDto
{
    public Guid Id { get; set; }

    [MapProperty("Destination.Street")]
    public string Street { get; set; } = "";

    public string DestinationCity { get; set; } = "";
}

/// <summary>A hierarchy in one table: the root (concrete) and two derived types, each with its own columns.</summary>
[Entity]
[Table("Charges")]
[Inheritance(Discriminator = "Kind")]
public class Charge : ISqlEntity
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = "";
}

[Entity]
public class CardCharge : Charge
{
    public string Last4 { get; set; } = "";
}

/// <summary>A third level: read as its own type through the root and through CardCharge, with CardCharge's columns and its own.</summary>
[Entity]
public sealed class PremiumCardCharge : CardCharge
{
    public int? Tier { get; set; }
}

[Entity]
public sealed class WireCharge : Charge
{
    public string Iban { get; set; } = "";
    public int Days { get; set; }
}

/// <summary>
///     A hierarchy with a table per concrete type: the derived types each in their own table (the inherited columns repeated), the
///     root's "Payments" a view over them that the generator writes (<c>PaymentView.CreateSql</c>), with the type's name as Kind.
///     Reads through the root come from the view; a write through the root goes to the row's own table.
/// </summary>
[Entity]
[Table("Payments")]
[Inheritance(Discriminator = "Kind", Strategy = InheritanceStrategy.Tpc)]
public abstract class Payment : ISqlEntity
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaidAt { get; set; }

    public Order Order { get; set; } = default!;
}

[Entity]
[Table("CardPayments")]
public sealed class CardPayment : Payment
{
    public string Last4 { get; set; } = "";
    public string Brand { get; set; } = "";
}

[Entity]
[Table("WirePayments")]
public sealed class WirePayment : Payment
{
    public string Iban { get; set; } = "";
    public int Days { get; set; }
}

/// <summary>A tree: a navigation to itself, and the generated walks <c>GetDescendantsByParent</c> / <c>GetAncestorsByParent</c>.</summary>
[Entity]
[Table("Categories")]
[GenerateHierarchy]
public sealed class Category : ISqlEntity
{
    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = "";

    public Category? Parent { get; set; }
}

/// <summary>What a JSON column holds, serialized through the source-generated context below: no reflection. A path into it is SQL in a lambda (<c>d.Spec.Pages</c>, <c>d.Spec.Author.Name</c>, <c>d.Spec.Tags.Contains(x)</c>).</summary>
public sealed class DocumentSpec
{
    public string Title { get; set; } = "";
    public List<string> Tags { get; set; } = [];
    public int Pages { get; set; }
    public DocumentAuthor? Author { get; set; }

    /// <summary>An array of objects: <c>d.Spec.Chapters.Any(c => c.Pages > 10)</c> tests its elements.</summary>
    public List<DocumentChapter> Chapters { get; set; } = [];
}

/// <summary>An element of the JSON document's array of objects.</summary>
public sealed class DocumentChapter
{
    public string Heading { get; set; } = "";
    public int Pages { get; set; }
}

/// <summary>A nested object of the JSON document; its member is serialized under another name, which the JSON path follows.</summary>
public sealed class DocumentAuthor
{
    [System.Text.Json.Serialization.JsonPropertyName("full_name")]
    public string Name { get; set; } = "";
}

[System.Text.Json.Serialization.JsonSerializable(typeof(DocumentSpec))]
public sealed partial class ExtrasJsonContext : System.Text.Json.Serialization.JsonSerializerContext;

/// <summary>A row with a JSON column: a converter to text, cast to jsonb on PostgreSQL.</summary>
[Entity]
[Table("Documents")]
public sealed class Document : ISqlEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";

    [Convert<SqlJson<DocumentSpec, ExtrasJsonContext>>]
    [Column("Spec", Cast = "jsonb")]
    public DocumentSpec Spec { get; set; } = new();
}

/// <summary>A view over the workload (customers with their order count), read like a table and never written: no writer, a command through a chain refused at compile time.</summary>
[Entity]
[Table("CustomerSummary", ReadOnly = true)]
public sealed class CustomerSummary : ISqlEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Country { get; set; } = "";
    public int Orders { get; set; }
}

/// <summary>What an assignment hangs off: the root names it by kind and key, each derived type carries the navigation to its own parent.</summary>
public enum ParentKind
{
    Customer = 0,
    Order = 1,
    Site = 2,
}

/// <summary>
///     A hierarchy whose root names its parent polymorphically (<see cref="ParentType" /> + <see cref="ParentId" />) and whose derived
///     types each declare the navigation to their own parent on that key: the shape of an association that may hang off several
///     entities (a type test in a lambda picks the branch, a cast reaches the derived type's navigation).
/// </summary>
[Entity]
[Table("Assignments")]
[Inheritance(InheritanceStrategy.Tph, DiscriminatorColumn = "Kind")] // the repository's form (Showcase's Fee)
public abstract class Assignment : ISqlEntity
{
    public Guid Id { get; set; }
    public Guid ParentId { get; set; }

    [Convert<SqlEnumName<ParentKind>>]
    public ParentKind ParentType { get; set; }

    public string Note { get; set; } = "";
}

/// <summary>An assignment to a customer: <see cref="Parent" /> joins Customers on the root's ParentId.</summary>
[Entity]
public sealed class CustomerAssignment : Assignment
{
    public Customer Parent { get; set; } = default!;
    public string Role { get; set; } = "";
}

/// <summary>An assignment to an order: the same ParentId, another parent.</summary>
[Entity]
public sealed class OrderAssignment : Assignment
{
    public Order Parent { get; set; } = default!;
}

/// <summary>An assignment to a site, an entity outside the model: reached by ParentType and ParentId only.</summary>
[Entity]
public sealed class SiteAssignment : Assignment
{
    public int Floor { get; set; }
}

/// <summary>A task hanging off an assignment of any kind: a visibility filter tests the assignment's type and walks to its parent.</summary>
[Entity]
[Table("Reminders")]
public sealed class Reminder : ISqlEntity
{
    public Guid Id { get; set; }
    public Guid AssignmentId { get; set; }
    public string Text { get; set; } = "";

    public Assignment Assignment { get; set; } = default!;
}

/// <summary>
///     Columns the database fills: a computed column over another (it changes with it on updates) and a default on insert. Never written, read
///     back. It tracks its changes (as <see cref="Ticket" />), so a save of a modified quantity reads the recomputed column back too.
/// </summary>
[Entity]
[Table("Gauges")]
public sealed class Gauge : ISqlEntity, IChangeTracking
{
    private readonly HashSet<string> _modified = [];

    public Guid Id { get; set; }
    public int Quantity { get; set; }

    [DatabaseGenerated(OnUpdate = true)]
    public int Doubled { get; set; }

    [DatabaseGenerated]
    public string Code { get; set; } = "";

    public void SetQuantity(int value)
    {
        if (Quantity == value)
            return;
        Quantity = value;
        _modified.Add(nameof(Quantity));
    }

    [NotMapped]
    public IReadOnlySet<string> ModifiedProperties => _modified;

    [NotMapped]
    public IReadOnlySet<string> CollectionsModified { get; } = new HashSet<string>();

    [NotMapped]
    public bool IsNew { get; set; }

    public void ResetModifiedProperties() => _modified.Clear();
}

/// <summary>
///     A hierarchy with a table per type: Vehicles holds every vehicle's plate and version, Cars and Trucks their own columns under the same
///     key; the reads go through the view VehiclesView (the generator's <c>VehicleView.CreateSql</c>), a write of a car is two statements.
/// </summary>
[Entity]
[Table("Vehicles")]
[Inheritance(InheritanceStrategy.Tpt, DiscriminatorColumn = "Kind")] // the repository's form: the strategy in the constructor, numbered differently there
public class Vehicle : ISqlEntity
{
    public Guid Id { get; set; }
    public string Plate { get; set; } = "";

    [Version]
    public int Version { get; set; }
}

[Entity]
[Table("Cars")]
public class Car : Vehicle
{
    public int Seats { get; set; }
}

/// <summary>A third level of the table-per-type hierarchy: a sports car's row is in Vehicles, Cars and SportsCars under one key.</summary>
[Entity]
[Table("SportsCars")]
public sealed class SportsCar : Car
{
    public int TopSpeed { get; set; }
}

[Entity]
[Table("Trucks")]
public sealed class Truck : Vehicle
{
    public decimal Payload { get; set; }
}

/// <summary>Array columns: text[], uuid[] and integer[] on PostgreSQL, the JSON text of the array elsewhere (<c>SqlArrays</c>).</summary>
[Entity]
[Table("Bundles")]
public sealed class Bundle : ISqlEntity
{
    public Guid Id { get; set; }
    public string[] Labels { get; set; } = [];
    public List<Guid> Refs { get; set; } = [];
    public int[] Scores { get; set; } = [];
}

/// <summary>A navigation to a composite key: one foreign key per key column, <c>{Navigation}{KeyProperty}</c> (TagOrderId, TagTag).</summary>
[Entity]
[Table("TagNotes")]
public sealed class TagNote : ISqlEntity
{
    public Guid Id { get; set; }
    public Guid TagOrderId { get; set; }
    public string TagTag { get; set; } = "";
    public string Text { get; set; } = "";

    public OrderTag? Tag { get; set; }
}
