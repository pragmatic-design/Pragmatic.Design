use lopdf::{Document, Object, ObjectId, Dictionary};
use std::collections::BTreeMap;

/// Merge multiple PDF byte slices into a single PDF.
pub fn merge_pdfs(pdfs: &[&[u8]]) -> Result<Vec<u8>, String> {
    if pdfs.is_empty() {
        return Err("No PDFs to merge".into());
    }
    if pdfs.len() == 1 {
        return Ok(pdfs[0].to_vec());
    }

    let documents: Vec<Document> = pdfs.iter()
        .enumerate()
        .map(|(i, bytes)| {
            Document::load_mem(bytes)
                .map_err(|e| format!("Failed to load PDF #{}: {e}", i + 1))
        })
        .collect::<Result<Vec<_>, _>>()?;

    let mut merged = Document::with_version("1.7");
    let mut all_page_ids: Vec<ObjectId> = Vec::new();

    for doc in &documents {
        let id_offset = merged.max_id + 1;

        // Map old object IDs → new (renumbered) IDs
        let mut id_map: BTreeMap<ObjectId, ObjectId> = BTreeMap::new();
        for &old_id in doc.objects.keys() {
            let new_id = (old_id.0 + id_offset, old_id.1);
            id_map.insert(old_id, new_id);
        }

        // Copy all objects with new IDs
        for (&old_id, object) in &doc.objects {
            let new_id = id_map[&old_id];
            let mut cloned = object.clone();
            remap_references(&mut cloned, &id_map);
            merged.objects.insert(new_id, cloned);
        }

        // Update max_id
        if let Some(&max) = id_map.values().map(|(n, _)| n).max() {
            if max > merged.max_id {
                merged.max_id = max;
            }
        }

        // Collect remapped page IDs (in page order)
        let pages = doc.get_pages();
        let mut sorted_pages: Vec<_> = pages.iter().collect();
        sorted_pages.sort_by_key(|(num, _)| *num);

        for (_, &page_id) in sorted_pages {
            if let Some(&new_id) = id_map.get(&page_id) {
                all_page_ids.push(new_id);
            }
        }
    }

    // Build Pages dictionary
    let kids: Vec<Object> = all_page_ids.iter()
        .map(|&id| Object::Reference(id))
        .collect();

    let mut pages_dict = Dictionary::new();
    pages_dict.set("Type", Object::Name(b"Pages".to_vec()));
    pages_dict.set("Count", Object::Integer(all_page_ids.len() as i64));
    pages_dict.set("Kids", Object::Array(kids));

    let pages_id = merged.add_object(Object::Dictionary(pages_dict));

    // Set Parent reference on each page
    for &page_id in &all_page_ids {
        if let Some(Object::Dictionary(dict)) = merged.objects.get_mut(&page_id) {
            dict.set("Parent", Object::Reference(pages_id));
        }
    }

    // Build Catalog
    let mut catalog = Dictionary::new();
    catalog.set("Type", Object::Name(b"Catalog".to_vec()));
    catalog.set("Pages", Object::Reference(pages_id));

    let catalog_id = merged.add_object(Object::Dictionary(catalog));
    merged.trailer.set("Root", Object::Reference(catalog_id));

    let mut output = Vec::new();
    merged.save_to(&mut output)
        .map_err(|e| format!("Failed to save merged PDF: {e}"))?;

    Ok(output)
}

/// Split a range of pages from a PDF (1-based, inclusive).
pub fn split_pdf(pdf: &[u8], from_page: u32, to_page: u32) -> Result<Vec<u8>, String> {
    let doc = Document::load_mem(pdf)
        .map_err(|e| format!("Failed to load PDF: {e}"))?;

    let page_count = doc.get_pages().len() as u32;

    if from_page < 1 || from_page > page_count {
        return Err(format!("from_page {from_page} out of range (1..{page_count})"));
    }
    if to_page < from_page || to_page > page_count {
        return Err(format!("to_page {to_page} out of range ({from_page}..{page_count})"));
    }

    let pages_to_delete: Vec<u32> = (1..=page_count)
        .filter(|p| *p < from_page || *p > to_page)
        .collect();

    let mut new_doc = doc.clone();
    new_doc.delete_pages(&pages_to_delete);

    let mut output = Vec::new();
    new_doc.save_to(&mut output)
        .map_err(|e| format!("Failed to save split PDF: {e}"))?;

    Ok(output)
}

/// Get the number of pages in a PDF.
pub fn page_count(pdf: &[u8]) -> Result<u32, String> {
    let doc = Document::load_mem(pdf)
        .map_err(|e| format!("Failed to load PDF: {e}"))?;

    Ok(doc.get_pages().len() as u32)
}

/// Recursively remap ObjectId references in a PDF object.
fn remap_references(object: &mut Object, id_map: &BTreeMap<ObjectId, ObjectId>) {
    match object {
        Object::Reference(id) => {
            if let Some(&new_id) = id_map.get(id) {
                *id = new_id;
            }
        }
        Object::Array(arr) => {
            for item in arr.iter_mut() {
                remap_references(item, id_map);
            }
        }
        Object::Dictionary(dict) => {
            for (_, value) in dict.iter_mut() {
                remap_references(value, id_map);
            }
        }
        Object::Stream(stream) => {
            for (_, value) in stream.dict.iter_mut() {
                remap_references(value, id_map);
            }
        }
        _ => {}
    }
}
