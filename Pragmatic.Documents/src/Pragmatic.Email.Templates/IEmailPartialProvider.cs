namespace Pragmatic.Email.Templates;

/// <summary>Provides email partial templates by name.</summary>
public interface IEmailPartialProvider
{
    ValueTask<EmailPartialDefinition?> GetAsync(string name, CancellationToken ct = default);
}
