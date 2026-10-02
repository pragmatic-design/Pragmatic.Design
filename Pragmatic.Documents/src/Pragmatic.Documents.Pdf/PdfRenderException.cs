namespace Pragmatic.Documents.Pdf;

/// <summary>Exception thrown when PDF rendering fails.</summary>
public sealed class PdfRenderException : Exception
{
    public PdfRenderException(string message) : base(message) { }

    public PdfRenderException(string message, Exception innerException) : base(message, innerException) { }
}
