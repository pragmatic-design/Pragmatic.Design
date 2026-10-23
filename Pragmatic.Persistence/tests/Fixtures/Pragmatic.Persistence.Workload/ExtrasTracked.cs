using Pragmatic.Persistence.Sql;

namespace Pragmatic.Persistence.Workload;

// The entities that track their own changes (IChangeTracking), with the aggregates a save walks: a ticket, a project and its
// labels (many-to-many), an invoice and its lines. Their tables come from ExtrasSchema, as Extras.cs's do.

/// <summary>
///     An entity that tracks its own changes, the repository's shape (<see cref="IChangeTracking" />; there the <c>Set{Property}</c>
///     methods are generated, here written by hand): a save inserts it while <see cref="IsNew" /> and updates the modified columns
///     only afterwards. The row of the repository's <c>SaveChangesBenchmarks</c>, reproduced in the harness.
/// </summary>
[Entity]
[Table("Tickets")]
public sealed class Ticket : ISqlEntity, IChangeTracking
{
    private readonly HashSet<string> _modified = [];

    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }

    /// <summary>Searched with Sql.FullText: FTS5 on SQLite, the full-text index on SQL Server (SqlFullText.CreateSql), tsvector on PostgreSQL.</summary>
    [FullText]
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public int Priority { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public void SetTitle(string value) => Track(nameof(Title), Title != value, () => Title = value);

    public void SetDescription(string value) => Track(nameof(Description), Description != value, () => Description = value);

    public void SetAmount(decimal value) => Track(nameof(Amount), Amount != value, () => Amount = value);

    public void SetPriority(int value) => Track(nameof(Priority), Priority != value, () => Priority = value);

    [NotMapped]
    public IReadOnlySet<string> ModifiedProperties => _modified;

    [NotMapped]
    public IReadOnlySet<string> CollectionsModified { get; } = new HashSet<string>();

    [NotMapped]
    public bool IsNew { get; set; }

    public void ResetModifiedProperties() => _modified.Clear();

    private void Track(string property, bool changed, Action set)
    {
        if (!changed)
            return;
        set();
        _modified.Add(property);
    }
}

/// <summary>A label, the other side of a many-to-many: linked to projects through the join table ProjectLabels (no entity of its own).</summary>
[Entity]
[Table("Labels")]
public sealed class Label : ISqlEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>
///     A tracked aggregate with a many-to-many side (<see cref="Labels" />, through the join table) and a one-to-many (<see cref="Tickets" />,
///     on the ticket's ProjectId): a graph save syncs the links to the list (<see cref="CollectionsModified" />), the labels themselves
///     stay as they are. The shape of the repository's <c>HasMany().WithMany()</c> configurations.
/// </summary>
[Entity]
[Table("Projects")]
public sealed class Project : ISqlEntity, IChangeTracking
{
    private readonly HashSet<string> _modified = [];
    private readonly HashSet<string> _collections = [];

    public Guid Id { get; set; }
    public string Name { get; set; } = "";

    [ManyToMany("ProjectLabels", "ProjectId", "LabelId")]
    public List<Label> Labels { get; set; } = [];

    public List<Ticket> Tickets { get; set; } = [];

    public void SetName(string value)
    {
        if (Name == value)
            return;
        Name = value;
        _modified.Add(nameof(Name));
    }

    public void AddLabel(Label label)
    {
        Labels.Add(label);
        _collections.Add(nameof(Labels));
    }

    public void RemoveLabel(Label label)
    {
        Labels.Remove(label);
        _collections.Add(nameof(Labels));
    }

    [NotMapped]
    public IReadOnlySet<string> ModifiedProperties => _modified;

    [NotMapped]
    public IReadOnlySet<string> CollectionsModified => _collections;

    [NotMapped]
    public bool IsNew { get; set; }

    public void ResetModifiedProperties()
    {
        _modified.Clear();
        _collections.Clear();
    }
}

/// <summary>
///     An aggregate that tracks its changes and those of its collection (the repository's shape: <c>CollectionsModified</c> names the
///     collections touched): a graph save writes the root's modified columns, deletes the lines no longer in the list, inserts the new
///     ones and updates the modified ones, in one command. A line added through <see cref="AddLine" /> gets its foreign key here.
/// </summary>
[Entity]
[Table("Invoices")]
public sealed class Invoice : ISqlEntity, IChangeTracking
{
    private readonly HashSet<string> _modified = [];
    private readonly HashSet<string> _collections = [];

    public Guid Id { get; set; }
    public string Number { get; set; } = "";
    public decimal Total { get; set; }

    [Version]
    public int Version { get; set; }

    /// <summary>The days the invoice is in force: a range, either end open; <c>Active.Contains(day)</c> is SQL in a lambda.</summary>
    public SqlDateRange Active { get; set; } = new();

    public List<InvoiceLine> Lines { get; set; } = [];

    public void SetNumber(string value)
    {
        if (Number == value)
            return;
        Number = value;
        _modified.Add(nameof(Number));
    }

    public void SetTotal(decimal value)
    {
        if (Total == value)
            return;
        Total = value;
        _modified.Add(nameof(Total));
    }

    public void AddLine(InvoiceLine line)
    {
        line.InvoiceId = Id;
        Lines.Add(line);
        _collections.Add(nameof(Lines));
    }

    public void RemoveLine(InvoiceLine line)
    {
        if (Lines.Remove(line))
            _collections.Add(nameof(Lines));
    }

    [NotMapped]
    public IReadOnlySet<string> ModifiedProperties => _modified;

    [NotMapped]
    public IReadOnlySet<string> CollectionsModified => _collections;

    [NotMapped]
    public bool IsNew { get; set; }

    public void ResetModifiedProperties()
    {
        _modified.Clear();
        _collections.Clear();
    }
}

/// <summary>A line of an invoice, tracked like its parent.</summary>
[Entity]
[Table("InvoiceLines")]
public sealed class InvoiceLine : ISqlEntity, IChangeTracking
{
    private readonly HashSet<string> _modified = [];

    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public string Sku { get; set; } = "";
    public int Quantity { get; set; }

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
