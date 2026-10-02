mod pdf;

use pragmatic_core::{copy_last_error, set_last_error};

/// The hash of the source this library was built from (see build.rs), as a NUL-terminated string.
/// The gate reads it out of the binary's bytes; the export is what keeps it in a stripped, LTO build.
static SOURCE_STAMP: &str = concat!(env!("PRAGMATIC_PDF_SOURCE_STAMP"), "\0");

/// The source stamp, `PRAGMATIC_PDF_SOURCE_HASH:<hex>` or `…:unstamped`.
#[unsafe(no_mangle)]
pub extern "C" fn pragmatic_pdf_source_stamp() -> *const u8 {
    SOURCE_STAMP.as_ptr()
}

/// Convert a DocumentModel JSON to PDF bytes via Typst.
/// Optionally accepts a resources JSON with base64-encoded images.
///
/// # Safety
/// All pointers must be valid.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn pragmatic_doc_to_pdf(
    json: *const u8,
    json_len: usize,
    out_pdf: *mut *mut u8,
    out_pdf_len: *mut usize,
) -> i32 {
    if json.is_null() || out_pdf.is_null() || out_pdf_len.is_null() {
        set_last_error("Null pointer argument");
        return -1;
    }

    let json_slice = unsafe { std::slice::from_raw_parts(json, json_len) };
    let json_str = match std::str::from_utf8(json_slice) {
        Ok(s) => s,
        Err(e) => { set_last_error(format!("Invalid UTF-8: {e}")); return -1; }
    };

    // Parse DocumentModel JSON
    let model: pdf::model::DocumentModel = match serde_json::from_str(json_str) {
        Ok(m) => m,
        Err(e) => { set_last_error(format!("JSON parse failed: {e}")); return -1; }
    };

    // Generate Typst markup
    let typst_markup = pdf::typst_gen::generate_typst(&model);

    // Compile to PDF
    match pdf::compile::compile_to_pdf(&typst_markup, &model.resources) {
        Ok(pdf_bytes) => {
            let len = pdf_bytes.len();
            let boxed = pdf_bytes.into_boxed_slice();
            let ptr = Box::into_raw(boxed) as *mut u8;
            unsafe { *out_pdf = ptr; *out_pdf_len = len; }
            0
        }
        Err(e) => { set_last_error(e); -1 }
    }
}

/// Merge multiple PDFs into one.
///
/// # Safety
/// `pdf_ptrs` must point to `count` valid pointers, `pdf_lens` to `count` lengths.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn pragmatic_pdf_merge(
    pdf_ptrs: *const *const u8,
    pdf_lens: *const usize,
    count: usize,
    out_pdf: *mut *mut u8,
    out_pdf_len: *mut usize,
) -> i32 {
    if pdf_ptrs.is_null() || pdf_lens.is_null() || out_pdf.is_null() || out_pdf_len.is_null() {
        set_last_error("Null pointer argument");
        return -1;
    }
    if count == 0 {
        set_last_error("No PDFs to merge");
        return -1;
    }

    let ptrs = unsafe { std::slice::from_raw_parts(pdf_ptrs, count) };
    let lens = unsafe { std::slice::from_raw_parts(pdf_lens, count) };

    let slices: Vec<&[u8]> = ptrs.iter().zip(lens.iter())
        .map(|(&ptr, &len)| unsafe { std::slice::from_raw_parts(ptr, len) })
        .collect();

    match pdf::operations::merge_pdfs(&slices) {
        Ok(result) => {
            let len = result.len();
            let boxed = result.into_boxed_slice();
            let ptr = Box::into_raw(boxed) as *mut u8;
            unsafe { *out_pdf = ptr; *out_pdf_len = len; }
            0
        }
        Err(e) => { set_last_error(e); -1 }
    }
}

/// Split a range of pages from a PDF (1-based, inclusive).
///
/// # Safety
/// All pointers must be valid.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn pragmatic_pdf_split(
    pdf: *const u8,
    pdf_len: usize,
    from_page: u32,
    to_page: u32,
    out_pdf: *mut *mut u8,
    out_pdf_len: *mut usize,
) -> i32 {
    if pdf.is_null() || out_pdf.is_null() || out_pdf_len.is_null() {
        set_last_error("Null pointer argument");
        return -1;
    }

    let data = unsafe { std::slice::from_raw_parts(pdf, pdf_len) };

    match pdf::operations::split_pdf(data, from_page, to_page) {
        Ok(result) => {
            let len = result.len();
            let boxed = result.into_boxed_slice();
            let ptr = Box::into_raw(boxed) as *mut u8;
            unsafe { *out_pdf = ptr; *out_pdf_len = len; }
            0
        }
        Err(e) => { set_last_error(e); -1 }
    }
}

/// Get the number of pages in a PDF.
///
/// # Safety
/// All pointers must be valid.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn pragmatic_pdf_page_count(
    pdf: *const u8,
    pdf_len: usize,
    out_count: *mut u32,
) -> i32 {
    if pdf.is_null() || out_count.is_null() {
        set_last_error("Null pointer argument");
        return -1;
    }

    let data = unsafe { std::slice::from_raw_parts(pdf, pdf_len) };

    match pdf::operations::page_count(data) {
        Ok(count) => {
            unsafe { *out_count = count; }
            0
        }
        Err(e) => { set_last_error(e); -1 }
    }
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn pragmatic_last_error(buf: *mut u8, buf_len: usize) -> i32 {
    copy_last_error(buf, buf_len)
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn pragmatic_free_buffer(ptr: *mut u8, len: usize) {
    unsafe { pragmatic_core::free_buffer(ptr, len); }
}
