use lopdf::{Dictionary, Document, Object, ObjectId};

use crate::pdf::operations::{add_page_tree, append_document};

/// One chunk of a long document, as `typst_pdf` wrote it.
pub(crate) struct Chunk {
    pub pdf: Vec<u8>,
    /// The level of each entry of the chunk's outline, in document order.
    pub heading_levels: Vec<usize>,
}

/// The chunks of one document as one PDF. Unlike `merge_pdfs`, which joins documents that were separate,
/// this one keeps what a single compile would have written once for the whole document: the first chunk's
/// document information, language and XMP metadata (with the page count corrected), and one outline,
/// nested by heading level across the chunks as `typst_pdf` nests it.
pub(crate) fn join_chunks(chunks: &[Chunk], total_pages: usize) -> Result<Vec<u8>, String> {
    if let [only] = chunks {
        return Ok(only.pdf.clone());
    }

    let mut merged = Document::with_version("1.7");
    let mut page_ids = Vec::new();
    let mut entries: Vec<OutlineEntry> = Vec::new();
    let mut first: Option<(Dictionary, Option<Object>, Option<Object>)> = None;

    for (index, chunk) in chunks.iter().enumerate() {
        let doc = Document::load_mem(&chunk.pdf)
            .map_err(|e| format!("Failed to read chunk {} of the document: {e}", index + 1))?;
        let (id_map, pages) = append_document(&mut merged, &doc);
        page_ids.extend(pages);

        let root = doc.trailer.get(b"Root").and_then(Object::as_reference)
            .map_err(|e| format!("Chunk {} of the document has no catalog: {e}", index + 1))?;
        let catalog = merged.get_dictionary(id_map[&root]).map_err(|e| e.to_string())?.clone();

        let items = outline_items(&merged, &catalog);
        if items.len() != chunk.heading_levels.len() {
            return Err(format!(
                "Chunk {} of the document has {} outline entries for {} headings.",
                index + 1, items.len(), chunk.heading_levels.len()));
        }
        for (id, &level) in items.iter().zip(&chunk.heading_levels) {
            let item = merged.get_dictionary(*id).map_err(|e| e.to_string())?;
            entries.push(OutlineEntry {
                title: item.get(b"Title").ok().cloned(),
                dest: item.get(b"Dest").ok().cloned(),
                level,
                children: Vec::new(),
            });
        }

        if first.is_none() {
            let remap = |key: &[u8]| doc.trailer.get(key).ok().map(|value| match value {
                Object::Reference(id) => Object::Reference(id_map.get(id).copied().unwrap_or(*id)),
                other => other.clone(),
            });
            first = Some((catalog, remap(b"Info"), remap(b"ID")));
        }
    }

    let (first_catalog, info, id) = first.ok_or("No chunks to join")?;
    let pages_id = add_page_tree(&mut merged, &page_ids);

    let mut catalog = Dictionary::new();
    for (key, value) in first_catalog.iter() {
        if !matches!(key.as_slice(), b"Pages" | b"Outlines") {
            catalog.set(key.clone(), value.clone());
        }
    }
    catalog.set("Pages", Object::Reference(pages_id));
    if let Some(outlines) = add_outline(&mut merged, entries) {
        catalog.set("Outlines", Object::Reference(outlines));
    }
    if let Ok(Object::Reference(metadata)) = first_catalog.get(b"Metadata") {
        set_xmp_page_count(&mut merged, *metadata, total_pages);
    }

    let catalog_id = merged.add_object(Object::Dictionary(catalog));
    merged.trailer.set("Root", Object::Reference(catalog_id));
    if let Some(info) = info { merged.trailer.set("Info", info); }
    if let Some(id) = id { merged.trailer.set("ID", id); }

    // Each chunk's own catalog, page tree and outline are still in the file, now referenced by nothing.
    merged.prune_objects();

    let mut output = Vec::new();
    merged.save_to(&mut output).map_err(|e| format!("Failed to save the joined PDF: {e}"))?;
    Ok(output)
}

struct OutlineEntry {
    title: Option<Object>,
    dest: Option<Object>,
    level: usize,
    children: Vec<usize>,
}

/// The items of the outline `catalog` points to, depth first: the order of the headings they stand for.
fn outline_items(doc: &Document, catalog: &Dictionary) -> Vec<ObjectId> {
    fn walk(doc: &Document, first: Option<ObjectId>, items: &mut Vec<ObjectId>) {
        let mut next = first;
        while let Some(id) = next {
            items.push(id);
            let Ok(item) = doc.get_dictionary(id) else { return };
            walk(doc, item.get(b"First").and_then(Object::as_reference).ok(), items);
            next = item.get(b"Next").and_then(Object::as_reference).ok();
        }
    }

    let mut items = Vec::new();
    let root = catalog.get(b"Outlines").and_then(Object::as_reference).ok()
        .and_then(|id| doc.get_dictionary(id).ok());
    if let Some(root) = root {
        walk(doc, root.get(b"First").and_then(Object::as_reference).ok(), &mut items);
    }
    items
}

/// An outline over every chunk's entries: an entry goes under the latest entry of a lower level, which is
/// how `typst_pdf` nests the headings of one compile. Returns the outline's number, `None` without entries.
fn add_outline(doc: &mut Document, mut entries: Vec<OutlineEntry>) -> Option<ObjectId> {
    if entries.is_empty() {
        return None;
    }

    let mut top = Vec::new();
    let mut path: Vec<usize> = Vec::new();
    for index in 0..entries.len() {
        while path.last().is_some_and(|&last| entries[last].level >= entries[index].level) {
            path.pop();
        }
        match path.last() {
            Some(&parent) => entries[parent].children.push(index),
            None => top.push(index),
        }
        path.push(index);
    }

    let root = doc.new_object_id();
    let ids: Vec<ObjectId> = entries.iter().map(|_| doc.new_object_id()).collect();
    let write_level = |doc: &mut Document, siblings: &[usize], parent: ObjectId, entries: &[OutlineEntry]| {
        for (position, &index) in siblings.iter().enumerate() {
            let entry = &entries[index];
            let mut item = Dictionary::new();
            item.set("Parent", Object::Reference(parent));
            if position > 0 { item.set("Prev", Object::Reference(ids[siblings[position - 1]])); }
            if let Some(&next) = siblings.get(position + 1) { item.set("Next", Object::Reference(ids[next])); }
            if let (Some(&first), Some(&last)) = (entry.children.first(), entry.children.last()) {
                item.set("First", Object::Reference(ids[first]));
                item.set("Last", Object::Reference(ids[last]));
                // Negative: the entry starts closed, as typst_pdf writes it.
                item.set("Count", Object::Integer(-(entry.children.len() as i64)));
            }
            if let Some(title) = &entry.title { item.set("Title", title.clone()); }
            if let Some(dest) = &entry.dest { item.set("Dest", dest.clone()); }
            doc.objects.insert(ids[index], Object::Dictionary(item));
        }
    };

    write_level(doc, &top, root, &entries);
    for index in 0..entries.len() {
        let children = entries[index].children.clone();
        write_level(doc, &children, ids[index], &entries);
    }

    let mut outlines = Dictionary::new();
    outlines.set("Type", Object::Name(b"Outlines".to_vec()));
    outlines.set("First", Object::Reference(ids[top[0]]));
    outlines.set("Last", Object::Reference(ids[*top.last().unwrap()]));
    outlines.set("Count", Object::Integer(top.len() as i64));
    doc.objects.insert(root, Object::Dictionary(outlines));
    Some(root)
}

/// `typst_pdf` writes the page count into the XMP metadata (`xmpTPg:NPages`); a chunk's is its own.
fn set_xmp_page_count(doc: &mut Document, metadata: ObjectId, total_pages: usize) {
    const OPEN: &str = "<xmpTPg:NPages>";
    const CLOSE: &str = "</xmpTPg:NPages>";
    let Ok(Object::Stream(stream)) = doc.get_object_mut(metadata) else { return };
    if stream.dict.has(b"Filter") {
        return;
    }
    let Ok(xmp) = std::str::from_utf8(&stream.content) else { return };
    let Some(open) = xmp.find(OPEN) else { return };
    let Some(close) = xmp[open..].find(CLOSE).map(|c| open + c) else { return };
    let patched = format!("{}{OPEN}{total_pages}{}", &xmp[..open], &xmp[close..]);
    stream.set_content(patched.into_bytes());
}
