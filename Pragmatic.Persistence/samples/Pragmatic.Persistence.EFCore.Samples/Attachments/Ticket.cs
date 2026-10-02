using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.EFCore.Samples.Attachments;

/// <summary>One of the owner types a <see cref="CommentNote"/> can be attached to.</summary>
[Entity]
public partial class Ticket : IEntity
{
    public Guid PersistenceId { get; set; } = Guid.CreateVersion7();
    public Guid Id => PersistenceId;
    public string Title { get; set; } = "";
}
