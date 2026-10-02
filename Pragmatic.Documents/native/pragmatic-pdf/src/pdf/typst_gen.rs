use std::fmt::Write;
use crate::pdf::model::*;
use crate::pdf::style::{font_list, inherit, write_aligned, write_run};

/// Generate Typst markup from a DocumentModel.
pub fn generate_typst(model: &DocumentModel) -> String {
    let mut out = String::with_capacity(4096);

    // Page setup
    write_page_setup(&mut out, model);

    // Document metadata
    let doc_filename = model.title.as_deref().unwrap_or("Document");
    writeln!(out, "#let pragmatic-filename = \"{}\"", escape_str(doc_filename)).unwrap();
    if let Some(title) = &model.title {
        writeln!(out, "#set document(title: \"{}\")", escape_str(title)).unwrap();
    }
    if let Some(author) = &model.author {
        writeln!(out, "#set document(author: \"{}\")", escape_str(author)).unwrap();
    }

    // Pages
    for (i, page) in model.pages.iter().enumerate() {
        if i > 0 {
            writeln!(out, "#pagebreak()").unwrap();
        }

        // Header
        if let Some(header) = &page.header {
            write!(out, "#set page(header: [").unwrap();
            write_nodes(&mut out, header);
            writeln!(out, "])").unwrap();
        }

        // Footer
        if let Some(footer) = &page.footer {
            write!(out, "#set page(footer: [").unwrap();
            write_nodes(&mut out, footer);
            writeln!(out, "])").unwrap();
        }

        // Content
        write_nodes(&mut out, &page.content);
    }

    out
}

fn write_page_setup(out: &mut String, model: &DocumentModel) {
    let (w, h) = page_dimensions(model.page_size, model.orientation);
    writeln!(out,
        "#set page(width: {}mm, height: {}mm, margin: (top: {}mm, right: {}mm, bottom: {}mm, left: {}mm))",
        w, h, model.margins.top, model.margins.right, model.margins.bottom, model.margins.left
    ).unwrap();

    // Default text settings
    writeln!(out, "#set text(font: {}, size: 11pt)", font_list(None)).unwrap();
    writeln!(out).unwrap();
}

fn page_dimensions(size: PageSize, orientation: PageOrientation) -> (f64, f64) {
    let (w, h) = match size {
        PageSize::A4 => (210.0, 297.0),
        PageSize::A3 => (297.0, 420.0),
        PageSize::A5 => (148.0, 210.0),
        PageSize::Letter => (215.9, 279.4),
        PageSize::Legal => (215.9, 355.6),
        PageSize::Custom => (210.0, 297.0),
    };
    match orientation {
        PageOrientation::Portrait => (w, h),
        PageOrientation::Landscape => (h, w),
    }
}

fn write_nodes(out: &mut String, nodes: &[DocumentNode]) {
    for node in nodes {
        write_node(out, node);
    }
}

fn write_node(out: &mut String, node: &DocumentNode) {
    match node {
        DocumentNode::Text { content, style } => {
            write_aligned(out, style.as_ref(), |out| {
                write_run(out, style.as_ref(), |out| out.push_str(&escape(content)));
                writeln!(out).unwrap();
            });
        }
        DocumentNode::Heading { content, level, children, style } => {
            let prefix = "=".repeat(*level as usize);
            // The heading's run formatting wraps its text: an explicit `#text` wins over the heading's
            // own size and weight, and a heading's text has no other place a style could go.
            write_aligned(out, style.as_ref(), |out| {
                write!(out, "{} ", prefix).unwrap();
                if children.is_empty() {
                    write_run(out, style.as_ref(), |out| out.push_str(&escape(content)));
                } else {
                    // On the heading's own line, each child a run that completes the heading's style.
                    for child in children {
                        write_inline(out, child, style.as_ref());
                    }
                }
                writeln!(out).unwrap();
            });
        }
        DocumentNode::Paragraph { children, style } => {
            write_aligned(out, style.as_ref(), |out| {
                for child in children {
                    match child {
                        // A text child is a run of the paragraph: its own alignment does not apply, as in
                        // DOCX, and the paragraph's run formatting completes its own.
                        DocumentNode::Text { content, style: run } => {
                            write_run(out, inherit(style.as_ref(), run.as_ref()).as_ref(), |out| out.push_str(&escape(content)));
                            writeln!(out).unwrap();
                        }
                        DocumentNode::Hyperlink { href, children, style: link } =>
                            write_hyperlink(out, href, children, link.as_ref(), style.as_ref()),
                        _ => write_node(out, child),
                    }
                }
            });
            writeln!(out).unwrap();
        }
        DocumentNode::Image { source, width, height, style, .. } => {
            // Resource prefix: "resource:name" → references a virtual file registered in World
            let file_name = if source.starts_with("resource:") {
                format!("{}.png", &source[9..])
            } else {
                source.clone()
            };
            write_aligned(out, style.as_ref(), |out| {
                write!(out, "#image(\"{}\"", escape_str(&file_name)).unwrap();
                if let Some(w) = width { write!(out, ", width: {}mm", w).unwrap(); }
                if let Some(h) = height { write!(out, ", height: {}mm", h).unwrap(); }
                writeln!(out, ")").unwrap();
            });
        }
        DocumentNode::Table { columns, header, rows, repeat_header, .. } => {
            write_table(out, columns, header.as_ref(), rows, *repeat_header);
        }
        DocumentNode::List { ordered, items } => {
            for item in items {
                let marker = if *ordered { "+" } else { "-" };
                write!(out, "{} ", marker).unwrap();
                for child in &item.content {
                    write_inline(out, child, None);
                }
                writeln!(out).unwrap();
            }
            writeln!(out).unwrap();
        }
        DocumentNode::HorizontalRule { .. } => {
            writeln!(out, "#line(length: 100%)").unwrap();
        }
        DocumentNode::Spacer { height } => {
            writeln!(out, "#v({}mm)", height).unwrap();
        }
        DocumentNode::Container { children } => {
            write_nodes(out, children);
        }
        DocumentNode::PageBreak => {
            writeln!(out, "#pagebreak()").unwrap();
        }
        DocumentNode::Barcode { value, width, height, .. } => {
            // Barcode images are pre-generated by C# (Pragmatic.Imaging) and passed as resources.
            // Resource name convention: "barcode-{sanitized-value}"
            let w = width.unwrap_or(30.0);
            let h = height.unwrap_or(30.0);
            let resource_key = sanitize_resource_name(value);
            writeln!(out, "#image(\"{}.png\", width: {}mm, height: {}mm)", resource_key, w, h).unwrap();
        }
        DocumentNode::Hyperlink { href, children, style } => write_hyperlink(out, href, children, style.as_ref(), None),
        DocumentNode::Toc { max_level, title } => {
            if let Some(t) = title {
                writeln!(out, "= {}", escape(t)).unwrap();
            }
            writeln!(out, "#outline(depth: {})", max_level).unwrap();
        }
        DocumentNode::Footnote { content } => {
            write!(out, "#footnote[{}]", escape(content)).unwrap();
        }
        DocumentNode::Field { field_type, format } => {
            match field_type {
                FieldType::Page => write!(out, "#context counter(page).display()").unwrap(),
                FieldType::NumPages => write!(out, "#context counter(page).final().first()").unwrap(),
                FieldType::Date => {
                    if let Some(fmt) = format {
                        write!(out, "#datetime.today().display(\"{}\")", escape_str(fmt)).unwrap();
                    } else {
                        write!(out, "#datetime.today().display()").unwrap();
                    }
                }
                FieldType::Time => write!(out, "#datetime.today().display(\"[hour]:[minute]\")").unwrap(),
                FieldType::FileName => write!(out, "#pragmatic-filename").unwrap(),
            }
            writeln!(out).unwrap();
        }
        DocumentNode::Bookmark { children, .. } => {
            // Typst labels are placed after content; just render children
            write_nodes(out, children);
        }
    }
}

fn write_table(out: &mut String, columns: &[TableColumn], header: Option<&TableRow>, rows: &[TableRow], repeat_header: bool) {
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
            write_table_cell(out, cell, true);
        }
        if repeat_header {
            writeln!(out, "  ),").unwrap();
        }
    }

    // Data rows
    for row in rows {
        for cell in &row.cells {
            write_table_cell(out, cell, false);
        }
    }

    writeln!(out, ")").unwrap();
}

fn write_table_cell(out: &mut String, cell: &TableCell, is_header: bool) {
    let has_span = cell.col_span > 1 || cell.row_span > 1;

    if has_span {
        write!(out, "  table.cell(").unwrap();
        if cell.col_span > 1 { write!(out, "colspan: {}, ", cell.col_span).unwrap(); }
        if cell.row_span > 1 { write!(out, "rowspan: {}, ", cell.row_span).unwrap(); }
        write!(out, ")[").unwrap();
    } else {
        write!(out, "  [").unwrap();
    }

    if is_header {
        write!(out, "#strong[").unwrap();
        write_cell_content(out, &cell.content);
        write!(out, "]").unwrap();
    } else {
        write_cell_content(out, &cell.content);
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

/// A link as a block, aligned by its own style; `inherited` is the enclosing paragraph's style, which the
/// link's run formatting completes for the text inside it.
fn write_hyperlink(out: &mut String, href: &str, children: &[DocumentNode], style: Option<&NodeStyle>, inherited: Option<&NodeStyle>) {
    let link_style = inherit(inherited, style);
    write_aligned(out, style, |out| {
        write!(out, "#link(\"{}\")[", escape_str(href)).unwrap();
        for child in children {
            write_inline(out, child, link_style.as_ref());
        }
        writeln!(out, "]").unwrap();
    });
}

/// `inherited` is the enclosing block's style: its run formatting reaches a text and a link.
fn write_inline(out: &mut String, node: &DocumentNode, inherited: Option<&NodeStyle>) {
    match node {
        DocumentNode::Text { content, style } =>
            write_run(out, inherit(inherited, style.as_ref()).as_ref(), |out| out.push_str(&escape(content))),
        DocumentNode::Heading { content, children, style, .. } => {
            write!(out, "#strong[").unwrap();
            if children.is_empty() {
                out.push_str(&escape(content));
            } else {
                let heading_style = inherit(inherited, style.as_ref());
                for child in children {
                    write_inline(out, child, heading_style.as_ref());
                }
            }
            write!(out, "]").unwrap();
        }
        DocumentNode::Hyperlink { href, children, style } => {
            let link_style = inherit(inherited, style.as_ref());
            write!(out, "#link(\"{}\")[", escape_str(href)).unwrap();
            for child in children {
                write_inline(out, child, link_style.as_ref());
            }
            write!(out, "]").unwrap();
        }
        DocumentNode::Footnote { content } => {
            write!(out, "#footnote[{}]", escape(content)).unwrap();
        }
        _ => write_node(out, node),
    }
}

/// Sanitize a value (URL, text) into a safe resource file name.
pub fn sanitize_resource_name(value: &str) -> String {
    value.chars().map(|c| match c {
        'a'..='z' | 'A'..='Z' | '0'..='9' | '-' | '_' => c,
        _ => '-',
    }).collect()
}

fn escape(s: &str) -> String {
    // Escape Typst special characters in content (outside string literals)
    s.replace('\\', "\\\\")
     .replace('#', "\\#")
     .replace('$', "\\$")
     .replace('[', "\\[")
     .replace(']', "\\]")
     .replace('<', "\\<")
     .replace('>', "\\>")
     .replace('@', "\\@")
     .replace('*', "\\*")
     .replace('_', "\\_")
     .replace('`', "\\`")
}

/// Escape a value for use inside Typst `"..."` string literals.
pub(crate) fn escape_str(s: &str) -> String {
    s.replace('\\', "\\\\")
     .replace('"', "\\\"")
}
