use crate::pdf::operations::{merge_pdfs, page_count, split_pdf};

/// A one-page PDF whose fourth object is `depth` arrays nested inside each other. The PDFs these
/// operations read come from the caller, and a stack overflow in a library loaded into a .NET host is
/// not an error the host can catch: it ends the process. So a document nested this deep has to come
/// back as a result, whatever the result is.
fn deeply_nested_pdf(depth: usize) -> Vec<u8> {
    let nested = format!("{}{}", "[".repeat(depth), "]".repeat(depth));
    let objects = [
        "<< /Type /Catalog /Pages 2 0 R >>".to_string(),
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>".to_string(),
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Extra 4 0 R >>".to_string(),
        nested,
    ];

    let mut pdf = b"%PDF-1.7\n".to_vec();
    let mut offsets = Vec::new();
    for (i, body) in objects.iter().enumerate() {
        offsets.push(pdf.len());
        pdf.extend_from_slice(format!("{} 0 obj\n{body}\nendobj\n", i + 1).as_bytes());
    }
    let xref = pdf.len();
    pdf.extend_from_slice(format!("xref\n0 {}\n0000000000 65535 f \n", objects.len() + 1).as_bytes());
    for offset in offsets {
        pdf.extend_from_slice(format!("{offset:010} 00000 n \n").as_bytes());
    }
    pdf.extend_from_slice(
        format!("trailer\n<< /Size {} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n", objects.len() + 1).as_bytes());
    pdf
}

const DEPTH: usize = 100_000;

#[test]
fn page_count_returns_on_a_deeply_nested_document() {
    let _ = page_count(&deeply_nested_pdf(DEPTH));
}

#[test]
fn merge_returns_on_a_deeply_nested_document() {
    let nested = deeply_nested_pdf(DEPTH);
    let _ = merge_pdfs(&[&nested, &nested]);
}

#[test]
fn split_returns_on_a_deeply_nested_document() {
    let _ = split_pdf(&deeply_nested_pdf(DEPTH), 1, 1);
}

/// The control case: the same document without the nesting is read, so the three tests above reach the
/// parser and are not rejected before it for a malformed file.
#[test]
fn the_same_document_shallow_is_read() {
    assert_eq!(page_count(&deeply_nested_pdf(3)), Ok(1));
}
