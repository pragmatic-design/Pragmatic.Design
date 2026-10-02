using Pragmatic.Documents.Docx.Internal;
using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Docx;

/// <summary>
/// Renders a <see cref="DocumentModel"/> to DOCX bytes via OOXML generation.
/// Zero external dependencies — uses ZipArchive + XmlWriter.
/// Stateless static utility, consistent with <c>XlsxRenderer</c> and <c>PdfRenderer</c>.
/// </summary>
public static class DocxRenderer
{
    public static byte[] Render(DocumentModel model, DocxResources? resources = null, DocxRenderOptions? options = null)
        => DocxPackageWriter.Build(model, resources, options ?? DocxRenderOptions.Default);

    public static void RenderTo(Stream output, DocumentModel model, DocxResources? resources = null, DocxRenderOptions? options = null)
        => DocxPackageWriter.BuildTo(output, model, resources, options ?? DocxRenderOptions.Default);

    public static Task<byte[]> RenderAsync(DocumentModel model, DocxResources? resources = null, DocxRenderOptions? options = null, CancellationToken ct = default)
        => Task.Run(() => Render(model, resources, options), ct);

    public static Task RenderToStreamAsync(Stream output, DocumentModel model, DocxResources? resources = null, DocxRenderOptions? options = null, CancellationToken ct = default)
        => Task.Run(() => DocxPackageWriter.BuildTo(output, model, resources, options ?? DocxRenderOptions.Default), ct);
}
