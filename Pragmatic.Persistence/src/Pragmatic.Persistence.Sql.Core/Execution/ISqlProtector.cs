namespace Pragmatic.Persistence.Sql.Execution;

/// <summary>Turns a value into what the column stores and back: an encryption, with whatever key the application chose (per tenant, rotated).</summary>
public interface ISqlProtector
{
    string Protect(string value);

    string Unprotect(string stored);
}
