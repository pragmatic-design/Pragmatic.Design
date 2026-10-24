namespace Pragmatic.Persistence.Sql.Attributes;

/// <summary>
///     On the key property (<c>int</c> or <c>long</c>): the database assigns it on insert (identity, serial,
///     AUTOINCREMENT). The insert leaves the column out and reads the value back into the object, one row or a batch
///     (<c>RETURNING</c> / <c>OUTPUT INSERTED</c>); an upsert needs a client key and is refused.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class IdentityAttribute : Attribute;
