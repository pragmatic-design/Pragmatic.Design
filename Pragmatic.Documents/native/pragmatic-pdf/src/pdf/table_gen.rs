use std::fmt::Write;
use crate::pdf::model::*;
use crate::pdf::typst_gen::write_inline;

/// `marked_from`, when set, is the index in the whole table of `rows[0]`: each row then carries invisible
/// markers that say where it starts and where each of its cells ends, which is how a chunked render finds a
/// page that a row begins cleanly (see `chunked`).
pub(super) fn write_table(out: &mut String, columns: &[TableColumn], header: Option<&TableRow>, rows: &[TableRow], repeat_header: bool, marked_from: Option<usize>) {
    let col_count = columns.len().max(
        header.map_or(0, |h| h.cells.len()).max(
            rows.first().map_or(0, |r| r.cells.len())
        )
    );

    if col_count == 0 { return; }

    // Column definitions
    write!(out, "#table(\n  columns: (").unwrap();
    for (i, col) in columns.iter().enumerate() {
        if i > 0 { write!(out, ", ").unwrap(); }
        if let Some(w) = col.width {
            write!(out, "{}mm", w).unwrap();
        } else {
            write!(out, "1fr").unwrap();
        }
    }
    // Fill remaining columns with auto
    for i in columns.len()..col_count {
        if i > 0 || !columns.is_empty() { write!(out, ", ").unwrap(); }
        write!(out, "1fr").unwrap();
    }
    writeln!(out, "),").unwrap();

    // Alignment
    let has_align = columns.iter().any(|c| c.align.is_some());
    if has_align {
        write!(out, "  align: (").unwrap();
        for (i, col) in columns.iter().enumerate() {
            if i > 0 { write!(out, ", ").unwrap(); }
            match col.align {
                Some(TextAlign::Center) => write!(out, "center").unwrap(),
                Some(TextAlign::Right) => write!(out, "right").unwrap(),
                Some(TextAlign::Justify) => write!(out, "left").unwrap(),
                _ => write!(out, "left").unwrap(),
            }
        }
        writeln!(out, "),").unwrap();
    }

    // Header row — use table.header() for repeat support
    if let Some(header) = header {
        if repeat_header {
            writeln!(out, "  table.header(repeat: true,").unwrap();
        }
        for cell in &header.cells {
            write_table_cell(out, cell, true, None);
        }
        if repeat_header {
            writeln!(out, "  ),").unwrap();
        }
    }

    // Data rows
    for (i, row) in rows.iter().enumerate() {
        for (j, cell) in row.cells.iter().enumerate() {
            write_table_cell(out, cell, false, marked_from.map(|first| RowMarker { row: first + i, starts: j == 0 }));
        }
    }

    writeln!(out, ")").unwrap();
}

/// The labels of the markers `write_table` writes, which `chunked` queries.
pub(super) const ROW_START_LABEL: &str = "pragmatic-row-start";
pub(super) const CELL_END_LABEL: &str = "pragmatic-cell-end";

struct RowMarker { row: usize, starts: bool }

fn write_table_cell(out: &mut String, cell: &TableCell, is_header: bool, marker: Option<RowMarker>) {
    let has_span = cell.col_span > 1 || cell.row_span > 1;

    if has_span {
        write!(out, "  table.cell(").unwrap();
        if cell.col_span > 1 { write!(out, "colspan: {}, ", cell.col_span).unwrap(); }
        if cell.row_span > 1 { write!(out, "rowspan: {}, ", cell.row_span).unwrap(); }
        write!(out, ")[").unwrap();
    } else {
        write!(out, "  [").unwrap();
    }

    // `#metadata` is located but never drawn: the page is the same with or without it.
    if let Some(RowMarker { row, starts: true }) = marker {
        write!(out, "#metadata({row})<{ROW_START_LABEL}>").unwrap();
    }

    if is_header {
        write!(out, "#strong[").unwrap();
        write_cell_content(out, &cell.content);
        write!(out, "]").unwrap();
    } else {
        write_cell_content(out, &cell.content);
    }

    if let Some(RowMarker { row, .. }) = marker {
        write!(out, "#metadata({row})<{CELL_END_LABEL}>").unwrap();
    }

    if has_span {
        writeln!(out, "],").unwrap();
    } else {
        writeln!(out, "],").unwrap();
    }
}

fn write_cell_content(out: &mut String, nodes: &[DocumentNode]) {
    for node in nodes {
        write_inline(out, node, None);
    }
}
