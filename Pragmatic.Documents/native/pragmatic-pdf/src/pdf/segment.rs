use std::fmt::Write;
use crate::pdf::model::*;
use crate::pdf::table_gen::write_table;
use crate::pdf::typst_gen::{write_node, write_page_rules, write_preamble};

/// A place in the document: before node `node` of page `page`, or, when that node is a table, before its
/// row `row`. A chunk of a long document runs from one position to the next; every position but the
/// document's start is inside a table, since a table is the only node a chunk can end in.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) struct Position {
    pub page: usize,
    pub node: usize,
    pub row: usize,
}

impl Position {
    pub(crate) const START: Position = Position { page: 0, node: 0, row: 0 };
}

/// What a chunk's markup is made of besides its content.
pub(crate) struct ChunkMarkup {
    /// The number the chunk's first page carries.
    pub first_page: usize,
    /// The document's page count, written where a `FieldType::NumPages` field is.
    pub total_pages: usize,
    /// Write the row markers into the table the chunk ends in.
    pub markers: bool,
}

/// The rows of the table at `at`.
pub(crate) fn table_rows(model: &DocumentModel, at: Position) -> &[TableRow] {
    match &model.pages[at.page].content[at.node] {
        DocumentNode::Table { rows, .. } => rows,
        _ => &[],
    }
}

/// Where a chunk that starts at `start` should end so that it holds about `budget` cells: inside the first
/// table that takes the count past it, or `None` when the rest of the document fits.
pub(crate) fn plan_end(model: &DocumentModel, start: Position, budget: usize) -> Option<Position> {
    let mut spent = 0;
    for page in start.page..model.pages.len() {
        let content = &model.pages[page].content;
        let first_node = if page == start.page { start.node } else { 0 };
        for node in first_node..content.len() {
            match &content[node] {
                DocumentNode::Table { rows, .. } => {
                    let first_row = if page == start.page && node == start.node { start.row } else { 0 };
                    for (row, cells) in rows.iter().enumerate().skip(first_row) {
                        spent += cells.cells.iter().map(|c| 1 + cost(&c.content)).sum::<usize>();
                        if spent > budget && row + 1 < rows.len() {
                            return Some(Position { page, node, row: row + 1 });
                        }
                    }
                }
                other => spent += cost(std::slice::from_ref(other)),
            }
        }
    }
    None
}

/// How much a run of nodes weighs in a chunk's budget: one per node, nested ones included.
pub(crate) fn cost(nodes: &[DocumentNode]) -> usize {
    nodes.iter().map(|node| 1 + match node {
        DocumentNode::Heading { children, .. }
        | DocumentNode::Paragraph { children, .. }
        | DocumentNode::Container { children }
        | DocumentNode::Hyperlink { children, .. }
        | DocumentNode::Bookmark { children, .. } => cost(children),
        DocumentNode::List { items, .. } => items.iter().map(|i| cost(&i.content)).sum(),
        DocumentNode::Table { header, rows, .. } => header.iter().chain(rows)
            .flat_map(|r| &r.cells).map(|c| 1 + cost(&c.content)).sum(),
        _ => 0,
    }).sum()
}

/// Whether a chunk may start at row `row` of `rows`: no cell above it spans down past it.
pub(crate) fn can_cut_before(rows: &[TableRow], row: usize) -> bool {
    rows[..row].iter().enumerate()
        .all(|(r, cells)| cells.cells.iter().all(|c| r + (c.row_span.max(1) as usize) <= row))
}

/// The markup of the chunk from `start` to `end` (the document's end when `None`).
pub(crate) fn chunk_markup(model: &DocumentModel, start: Position, end: Option<Position>, chunk: &ChunkMarkup) -> String {
    let mut out = String::with_capacity(4096);
    write_preamble(&mut out, model, Some(chunk.total_pages));

    let last_page = end.map_or(model.pages.len().saturating_sub(1), |e| e.page);
    for page in start.page..=last_page.min(model.pages.len().saturating_sub(1)) {
        if page > start.page {
            writeln!(out, "#pagebreak()").unwrap();
        }
        let document_page = &model.pages[page];
        write_page_rules(&mut out, document_page);
        if page == start.page && chunk.first_page > 1 {
            // After the page rules: a page rule set after content would start a page of its own.
            writeln!(out, "#counter(page).update({})", chunk.first_page).unwrap();
        }

        let first_node = if page == start.page { start.node } else { 0 };
        let last_node = match end {
            Some(e) if e.page == page => e.node,
            _ => document_page.content.len().saturating_sub(1),
        };
        for node in first_node..=last_node.min(document_page.content.len().saturating_sub(1)) {
            let first_row = if page == start.page && node == start.node { start.row } else { 0 };
            let row_end = match end { Some(e) if e.page == page && e.node == node => Some(e.row), _ => None };
            match &document_page.content[node] {
                DocumentNode::Table { columns, header, rows, repeat_header, .. } if first_row > 0 || row_end.is_some() => {
                    let to = row_end.unwrap_or(rows.len());
                    // A table that carries on from the previous chunk shows its header only if it repeats.
                    let header = header.as_ref().filter(|_| first_row == 0 || *repeat_header);
                    let marked_from = (chunk.markers && row_end.is_some()).then_some(first_row);
                    write_table(&mut out, columns, header, &rows[first_row..to], *repeat_header, marked_from);
                }
                other => write_node(&mut out, other),
            }
        }
    }

    out
}
