using Pragmatic.Email.Model;

namespace Pragmatic.Documents.Email;

/// <summary>
/// Renders an <see cref="EmailModel"/> to HTML suitable for email clients.
/// </summary>
public interface IEmailRenderer
{
    /// <summary>Render to a complete HTML string.</summary>
    string Render(EmailModel model);

    /// <summary>Render to a TextWriter (streaming).</summary>
    void RenderTo(TextWriter output, EmailModel model);
}
