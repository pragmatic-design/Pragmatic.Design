use std::collections::BTreeMap;

use typst::foundations::{Label, Selector, Value};
use typst::utils::PicoStr;
use typst_library::layout::PagedDocument;

use crate::pdf::compile::{bookmarked_levels, evict, export, get_fonts, CachedFonts, Engine};
use crate::pdf::join::{join_chunks, Chunk};
use crate::pdf::model::*;
use crate::pdf::segment::{can_cut_before, chunk_markup, cost, plan_end, table_rows, ChunkMarkup, Position};
use crate::pdf::table_gen::{CELL_END_LABEL, ROW_START_LABEL};
use crate::pdf::typst_gen::generate_typst;

/// How much one compile lays out, counted by `segment::cost`: a table cell with a text in it is 2. Typst
/// memoizes its layout of every cell until the compile ends, about 20 KB a cell (#279): a document over
/// this is laid out in chunks of about this size, with the cache evicted between them, so the peak follows
/// the chunk and not the document. Each chunk also writes its own subset of the fonts into the PDF: smaller
/// chunks lower the peak further and make the file larger.
pub(crate) const CELL_BUDGET: usize = 10_000;

/// Render a document to PDF bytes, in chunks when it is long enough to need them.
pub fn render_document(model: &DocumentModel) -> Result<Vec<u8>, String> {
    render_with(model, get_fonts(), CELL_BUDGET).map(|(pdf, _chunks)| pdf)
}

/// The PDF, and how many compiles laid the document out: 1 when it was not chunked.
pub(crate) fn render_with(model: &DocumentModel, fonts: &CachedFonts, budget: usize) -> Result<(Vec<u8>, usize), String> {
    let engine = Engine::new(&model.resources, fonts)?;
    let result = if can_chunk(model, budget) {
        render_chunked(model, &engine, budget)
    } else {
        engine.layout(&generate_typst(model))
            .and_then(|document| export(&document, None))
            .map(|pdf| (pdf, 1))
    };
    evict();
    result
}

/// A document is laid out in chunks when it is over the budget, unless something in it reads the whole
/// document: an outline lists every heading, and a footnote can be set on the page after its reference,
/// which a cut would leave behind. Those still render, in one compile.
fn can_chunk(model: &DocumentModel, budget: usize) -> bool {
    let all = model.pages.iter().flat_map(|p| [p.header.as_deref(), p.footer.as_deref(), Some(p.content.as_slice())])
        .flatten();
    let mut total = 0;
    for nodes in all {
        if any_node(nodes, &|n| matches!(n, DocumentNode::Toc { .. } | DocumentNode::Footnote { .. })) {
            return false;
        }
        total += cost(nodes);
    }
    total > budget
}

fn render_chunked(model: &DocumentModel, engine: &Engine, budget: usize) -> Result<(Vec<u8>, usize), String> {
    // A "page N of M" needs M, which only the last chunk knows: with one in the document, each chunk is laid
    // out a second time once the count is known. A field is in the page's margin, so the second layout puts
    // every row where the first did; the page count of each chunk is checked against it all the same.
    let needs_total = model.pages.iter()
        .flat_map(|p| [p.header.as_deref(), p.footer.as_deref(), Some(p.content.as_slice())])
        .flatten()
        .any(|nodes| any_node(nodes, &|n| matches!(n, DocumentNode::Field { field_type: FieldType::NumPages, .. })));

    let mut cuts: Vec<Cut> = Vec::new();
    let mut chunks: Vec<Chunk> = Vec::new();
    let (mut start, mut first_page) = (Position::START, 1);
    loop {
        let markup = |end: Option<Position>| chunk_markup(model, start, end, &ChunkMarkup { first_page, total_pages: 0, markers: true });
        let mut chunk_budget = budget;
        let (end, document, pages) = loop {
            let Some(end) = plan_end(model, start, chunk_budget) else {
                let document = engine.layout(&markup(None))?;
                let pages = document.pages.len();
                break (None, document, pages);
            };
            let document = engine.layout(&markup(Some(end)))?;
            let slice_start = if end.page == start.page && end.node == start.node { start.row } else { 0 };
            match find_cut(&document, table_rows(model, end), slice_start) {
                Some((pages, row)) => break (Some(Position { row, ..end }), document, pages),
                // Every row of the chunk's last table is on one page: take more of it.
                None => { drop(document); evict(); chunk_budget *= 2; }
            }
        };

        if !needs_total {
            chunks.push(Chunk { pdf: export(&document, Some(pages))?, heading_levels: bookmarked_levels(&document, pages) });
        }
        cuts.push(Cut { start, end, first_page, pages });
        drop(document);
        evict();

        first_page += pages;
        match end {
            Some(end) => start = end,
            None => break,
        }
    }

    let total_pages = first_page - 1;
    if needs_total {
        for cut in &cuts {
            let markup = chunk_markup(model, cut.start, cut.end, &ChunkMarkup { first_page: cut.first_page, total_pages, markers: false });
            let document = engine.layout(&markup)?;
            if document.pages.len() != cut.pages {
                return Err(format!(
                    "A chunk of the document laid out on {} pages the second time and {} the first: the page count \
                     would not match the pages. Report this with the document that produced it.",
                    document.pages.len(), cut.pages));
            }
            chunks.push(Chunk { pdf: export(&document, None)?, heading_levels: bookmarked_levels(&document, cut.pages) });
            drop(document);
            evict();
        }
    }

    join_chunks(&chunks, total_pages).map(|pdf| (pdf, chunks.len()))
}

struct Cut {
    start: Position,
    end: Option<Position>,
    first_page: usize,
    pages: usize,
}

/// The pages to keep from a chunk and the row the next chunk starts at: the latest page that a row of the
/// chunk's last table begins cleanly, with every row before it ended on an earlier page and no cell above it
/// spanning down past it. `None` when no page after the first does.
fn find_cut(document: &PagedDocument, rows: &[TableRow], slice_start: usize) -> Option<(usize, usize)> {
    let starts = marked_pages(document, ROW_START_LABEL, false);
    let ends = marked_pages(document, CELL_END_LABEL, true);

    for page in (2..=document.pages.len()).rev() {
        let Some((&row, _)) = starts.iter().find(|(_, p)| **p == page) else { continue };
        if row <= slice_start || !can_cut_before(rows, row) {
            continue;
        }
        let ended_before = ends.range(slice_start..row).all(|(_, p)| *p < page);
        if ended_before {
            return Some((page - 1, row));
        }
    }
    None
}

/// Row → page of its marker; the latest page when a row has several.
fn marked_pages(document: &PagedDocument, label: &str, latest: bool) -> BTreeMap<usize, usize> {
    let mut pages = BTreeMap::new();
    let selector = Selector::Label(Label::new(PicoStr::intern(label)));
    for element in document.introspector.query(&selector).iter() {
        let (Ok(Value::Int(row)), Some(location)) = (element.get_by_name("value"), element.location()) else { continue };
        let page = document.introspector.page(location).get();
        pages.entry(row as usize)
            .and_modify(|p: &mut usize| if latest { *p = (*p).max(page) } else { *p = (*p).min(page) })
            .or_insert(page);
    }
    pages
}

fn any_node(nodes: &[DocumentNode], test: &dyn Fn(&DocumentNode) -> bool) -> bool {
    nodes.iter().any(|node| test(node) || match node {
        DocumentNode::Heading { children, .. }
        | DocumentNode::Paragraph { children, .. }
        | DocumentNode::Container { children }
        | DocumentNode::Hyperlink { children, .. }
        | DocumentNode::Bookmark { children, .. } => any_node(children, test),
        DocumentNode::List { items, .. } => items.iter().any(|i| any_node(&i.content, test)),
        DocumentNode::Table { header, rows, .. } => header.iter().chain(rows)
            .flat_map(|r| &r.cells).any(|c| any_node(&c.content, test)),
        _ => false,
    })
}
