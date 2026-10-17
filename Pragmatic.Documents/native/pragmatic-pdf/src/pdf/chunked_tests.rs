use std::collections::BTreeMap;

use lopdf::{Document, Object, ObjectId};
use serde_json::{json, Value};

use crate::pdf::chunked::render_with;
use crate::pdf::compile::{evict, export, get_fonts, Engine};
use crate::pdf::model::DocumentModel;
use crate::pdf::segment::{chunk_markup, ChunkMarkup, Position};

/// Small enough that a few hundred rows make several chunks.
const SMALL_BUDGET: usize = 400;
/// No document is over it: the one-compile render every chunked one is compared with.
const ONE_COMPILE: usize = usize::MAX;

#[derive(Clone, Copy)]
struct Shape {
    rows: usize,
    /// Every n-th line has a description long enough to break across a page; 0 for none.
    tall_every: usize,
    /// The footer says "of N", which needs the page count.
    total_in_footer: bool,
    /// Every n-th line starts a cell that spans three rows; 0 for none.
    span_every: usize,
}

const REPORT: Shape = Shape { rows: 420, tall_every: 23, total_in_footer: true, span_every: 0 };

fn text(content: &str) -> Value { json!({ "$type": "text", "content": content }) }
fn heading(content: &str, level: u8) -> Value { json!({ "$type": "heading", "content": content, "level": level }) }
fn field(kind: &str) -> Value { json!({ "$type": "field", "fieldType": kind }) }

fn table(prefix: &str, shape: Shape, rows: usize) -> Value {
    let tall = "A description long enough to wrap onto many lines of its cell. ".repeat(60);
    let header: Vec<Value> = ["No", "SKU", "Description", "Qty", "Net"].iter().map(|h| json!({ "content": [text(h)] })).collect();
    let lines: Vec<Value> = (0..rows).map(|r| {
        let description = if shape.tall_every > 0 && r % shape.tall_every == 0 { tall.clone() } else { format!("Item {r}") };
        let mut cells = vec![json!({ "content": [text(&format!("{prefix}{r}"))] })];
        // In a group, the first row's second cell spans the group and the other rows have none of their own.
        let span = shape.span_every;
        let in_group = span > 0 && r - r % span + span <= rows;
        if in_group && r % span == 0 {
            cells.push(json!({ "content": [text(&format!("Group {r}"))], "rowSpan": span }));
        } else if !in_group {
            cells.push(json!({ "content": [text(&format!("SKU-{r:05}"))] }));
        }
        cells.push(json!({ "content": [{ "$type": "paragraph", "children": [text(&description)] }] }));
        cells.push(json!({ "content": [text(&format!("{}", r % 13))] }));
        cells.push(json!({ "content": [] }));
        json!({ "cells": cells })
    }).collect();
    json!({
        "$type": "table",
        "columns": [{}, {}, { "width": 70 }, {}, {}],
        "header": { "cells": header },
        "rows": lines,
        "repeatHeader": true
    })
}

/// A report: an introduction, a long table, a closing section, and an appendix page with a second table.
fn report(shape: Shape) -> DocumentModel {
    let mut footer = vec![text("Page "), field("Page")];
    if shape.total_in_footer {
        footer.extend([text(" of "), field("NumPages")]);
    }
    serde_json::from_value(json!({
        "title": "Long report",
        "author": "Pragmatic",
        "pages": [
            {
                "footer": footer,
                "content": [
                    heading("Report", 1),
                    text("What follows is every line."),
                    heading("Lines", 2),
                    table("L", shape, shape.rows),
                    heading("Totals", 2),
                    text("The end of the lines."),
                ]
            },
            {
                "footer": footer,
                "content": [
                    heading("Appendix", 1),
                    table("A", shape, shape.rows / 2),
                    heading("Notes", 2),
                    text("The end of the appendix."),
                ]
            }
        ]
    })).unwrap()
}

struct Rendered {
    pdf: Vec<u8>,
    chunks: usize,
}

fn render(model: &DocumentModel, budget: usize) -> Rendered {
    let (pdf, chunks) = render_with(model, get_fonts(), budget).unwrap_or_else(|e| panic!("{e}"));
    Rendered { pdf, chunks }
}

/// Every page of the chunked render against the same page of the one-compile render: the same text, drawn
/// at the same place.
fn assert_same_pages(chunked: &Rendered, single: &Rendered) {
    assert!(chunked.chunks > 2, "the document was laid out in {} chunk(s): nothing was cut", chunked.chunks);
    let (chunked_pages, single_pages) = (drawn_text(&chunked.pdf), drawn_text(&single.pdf));
    assert!(single_pages.len() > 10, "the report is {} pages: too short to cut", single_pages.len());
    for (index, (c, s)) in chunked_pages.iter().zip(&single_pages).enumerate() {
        assert_drawn_alike(c, s, &format!("page {} differs", index + 1));
    }
    assert_eq!(chunked_pages.len(), single_pages.len(), "page count");
}

/// The same runs of text, each within two hundredths of a point of the other. A chunk's page is laid out
/// from another origin than the one compile's, and the float sums can land either side of a rounding: on
/// macOS one line came out 557.67 against 557.68 with every other position equal.
fn assert_drawn_alike(left: &[(i64, i64, String)], right: &[(i64, i64, String)], message: &str) {
    let alike = left.len() == right.len() && left.iter().zip(right).all(|((lx, ly, lt), (rx, ry, rt))| {
        lt == rt && (lx - rx).abs() <= 2 && (ly - ry).abs() <= 2
    });
    assert!(alike, "{message}\n  left: {left:?}\n right: {right:?}");
}

#[test]
fn a_chunked_report_has_every_page_the_one_compile_has() {
    let model = report(REPORT);

    assert_same_pages(&render(&model, SMALL_BUDGET), &render(&model, ONE_COMPILE));
}

/// Without "of N" a chunk is written from its first layout: the path with no second pass.
#[test]
fn a_chunked_report_without_a_page_count_has_every_page_the_one_compile_has() {
    let model = report(Shape { total_in_footer: false, ..REPORT });

    assert_same_pages(&render(&model, SMALL_BUDGET), &render(&model, ONE_COMPILE));
}

/// A cut never falls between the rows a cell spans.
#[test]
fn a_chunked_table_with_spanning_cells_has_every_page_the_one_compile_has() {
    let model = report(Shape { span_every: 3, tall_every: 0, ..REPORT });

    assert_same_pages(&render(&model, SMALL_BUDGET), &render(&model, ONE_COMPILE));
}

/// The markers that find a cut are drawn nowhere: a chunk with them is the same file as one without.
#[test]
fn the_row_markers_change_nothing_on_the_page() {
    let model = report(Shape { rows: 120, span_every: 3, ..REPORT });
    let end = Some(Position { page: 0, node: 3, row: 110 });
    let engine = Engine::new(&model.resources, get_fonts()).unwrap();
    let pdf = |markers| {
        let markup = chunk_markup(&model, Position::START, end, &ChunkMarkup { first_page: 1, total_pages: 9, markers });
        assert_eq!(markup.contains("<pragmatic-row-start>"), markers);
        let pdf = export(&engine.layout(&markup).unwrap(), None).unwrap();
        evict();
        pdf
    };

    let (marked, plain) = (drawn_text(&pdf(true)), drawn_text(&pdf(false)));

    assert!(plain.iter().map(Vec::len).sum::<usize>() > 1_000, "the chunk draws almost nothing");
    assert_eq!(marked.len(), plain.len(), "page count");
    for (page, (m, p)) in marked.iter().zip(&plain).enumerate() {
        assert_drawn_alike(m, p, &format!("page {} is drawn differently with the markers", page + 1));
    }
}

/// Every run of text each page draws: where on the page, rounded to a hundredth of a point, and what it
/// says, read through its font's ToUnicode map. Not the glyph numbers, which are each chunk's own font
/// subset's; and not the stream's structure, since a marker may wrap a cell in a group of its own.
fn drawn_text(pdf: &[u8]) -> Vec<Vec<(i64, i64, String)>> {
    use lopdf::content::Content;
    type Matrix = [f64; 6];
    fn multiply(a: Matrix, b: Matrix) -> Matrix {
        [a[0] * b[0] + a[1] * b[2], a[0] * b[1] + a[1] * b[3], a[2] * b[0] + a[3] * b[2],
         a[2] * b[1] + a[3] * b[3], a[4] * b[0] + a[5] * b[2] + b[4], a[4] * b[1] + a[5] * b[3] + b[5]]
    }
    let number = |o: &Object| o.as_float().map(f64::from).or_else(|_| o.as_i64().map(|i| i as f64)).unwrap();

    let doc = Document::load_mem(pdf).unwrap();
    doc.get_pages().values().map(|&page| {
        let fonts: BTreeMap<Vec<u8>, BTreeMap<u16, String>> = doc.get_page_fonts(page).unwrap().into_iter()
            .map(|(name, font)| (name, to_unicode(&doc, font))).collect();
        let content = Content::decode(&doc.get_page_content(page).unwrap()).unwrap();
        let identity = [1.0, 0.0, 0.0, 1.0, 0.0, 0.0];
        // The font is part of the graphics state: `Q` restores it with the matrix.
        let (mut ctm, mut stack, mut text_at, mut font, mut runs) = (identity, Vec::new(), identity, None, Vec::new());
        for op in content.operations {
            match op.operator.as_str() {
                "q" => stack.push((ctm, font)),
                "Q" => (ctm, font) = stack.pop().unwrap(),
                "cm" => ctm = multiply(std::array::from_fn(|i| number(&op.operands[i])), ctm),
                "Tm" => text_at = multiply(std::array::from_fn(|i| number(&op.operands[i])), ctm),
                "Tf" => font = op.operands[0].as_name().ok().and_then(|name| fonts.get(name)),
                "TJ" | "Tj" => {
                    let map = font.expect("text drawn before any font is set");
                    let strings: Vec<&Object> = match &op.operands[0] { Object::Array(parts) => parts.iter().collect(), other => vec![other] };
                    let text: String = strings.into_iter()
                        .filter_map(|part| part.as_str().ok())
                        .flat_map(|bytes| bytes.chunks(2).map(|g| u16::from_be_bytes([g[0], *g.get(1).unwrap_or(&0)])))
                        .map(|glyph| map.get(&glyph).cloned().unwrap_or_else(|| format!("<{glyph}>")))
                        .collect();
                    runs.push(((text_at[4] * 100.0).round() as i64, (text_at[5] * 100.0).round() as i64, text));
                }
                _ => {}
            }
        }
        runs
    }).collect()
}

/// Glyph → text, from a Type0 font's ToUnicode CMap: the `bfchar` pairs and `bfrange` runs Typst writes.
fn to_unicode(doc: &Document, font: &lopdf::Dictionary) -> BTreeMap<u16, String> {
    let mut map = BTreeMap::new();
    let Ok(id) = font.get(b"ToUnicode").and_then(Object::as_reference) else { return map };
    let Ok(Object::Stream(stream)) = doc.get_object(id) else { return map };
    let cmap = String::from_utf8_lossy(&stream.decompressed_content().unwrap_or_else(|_| stream.content.clone())).into_owned();
    let hex = |s: &str| -> Vec<u16> {
        let digits = s.trim_matches(|c| c == '<' || c == '>');
        (0..digits.len() / 4).map(|i| u16::from_str_radix(&digits[i * 4..i * 4 + 4], 16).unwrap()).collect()
    };
    let mut section = "";
    for line in cmap.lines() {
        let line = line.trim();
        if line.ends_with("beginbfchar") { section = "char"; continue; }
        if line.ends_with("beginbfrange") { section = "range"; continue; }
        if line.starts_with("end") { section = ""; continue; }
        let fields: Vec<&str> = line.split_whitespace().collect();
        match (section, fields.as_slice()) {
            ("char", [glyph, text]) => { map.insert(hex(glyph)[0], String::from_utf16_lossy(&hex(text))); }
            ("range", [from, to, text]) if text.starts_with('<') => {
                let (from, to, first) = (hex(from)[0], hex(to)[0], hex(text));
                for (offset, glyph) in (from..=to).enumerate() {
                    let mut units = first.clone();
                    *units.last_mut().unwrap() += offset as u16;
                    map.insert(glyph, String::from_utf16_lossy(&units));
                }
            }
            _ => {}
        }
    }
    map
}

#[test]
fn a_chunked_report_has_the_outline_of_the_one_compile() {
    let model = report(REPORT);

    let (chunked, single) = (render(&model, SMALL_BUDGET), render(&model, ONE_COMPILE));

    assert!(chunked.chunks > 2);
    let outline = outline(&single.pdf);
    assert_eq!(outline.iter().map(|(depth, title, _)| (*depth, title.as_str())).collect::<Vec<_>>(),
        [(0, "Report"), (1, "Lines"), (1, "Totals"), (0, "Appendix"), (1, "Notes")]);
    assert_eq!(self::outline(&chunked.pdf), outline);
}

#[test]
fn a_chunked_report_keeps_the_document_information_and_the_page_count() {
    let model = report(REPORT);

    let (chunked, single) = (render(&model, SMALL_BUDGET), render(&model, ONE_COMPILE));

    assert!(chunked.chunks > 2);
    let (c, s) = (Document::load_mem(&chunked.pdf).unwrap(), Document::load_mem(&single.pdf).unwrap());
    assert_eq!(info(&c), info(&s));
    assert_eq!(catalog_keys(&c), catalog_keys(&s));
    let pages = s.get_pages().len();
    assert_eq!(xmp_page_count(&c), Some(pages));
    assert_eq!(xmp_page_count(&s), Some(pages));
}

/// An outline lists every heading of the document and a footnote may be set a page later than its reference:
/// neither survives a cut, so such a document is laid out in one compile, however long.
#[test]
fn a_report_with_an_outline_or_a_footnote_is_laid_out_in_one_compile() {
    for extra in [json!({ "$type": "toc", "maxLevel": 2, "title": "Contents" }), json!({ "$type": "footnote", "content": "A note." })] {
        let model: DocumentModel = serde_json::from_value(
            json!({ "pages": [{ "content": [extra, table("L", REPORT, 200)] }] })).unwrap();

        let rendered = render(&model, SMALL_BUDGET);

        assert_eq!(rendered.chunks, 1);
        assert!(rendered.pdf == render(&model, ONE_COMPILE).pdf);
    }
}

/// (depth, title, page number) of every outline entry, depth first.
pub(super) fn outline(pdf: &[u8]) -> Vec<(usize, String, u32)> {
    let doc = Document::load_mem(pdf).unwrap();
    let page_numbers: BTreeMap<ObjectId, u32> = doc.get_pages().into_iter().map(|(n, id)| (id, n)).collect();
    let mut entries = Vec::new();
    fn walk(doc: &Document, pages: &BTreeMap<ObjectId, u32>, first: Option<ObjectId>, depth: usize, out: &mut Vec<(usize, String, u32)>) {
        let mut next = first;
        while let Some(id) = next {
            let item = doc.get_dictionary(id).unwrap();
            let title = match item.get(b"Title").unwrap() {
                Object::String(bytes, _) => String::from_utf8_lossy(bytes).into_owned(),
                other => format!("{other:?}"),
            };
            let page = item.get(b"Dest").and_then(Object::as_array).ok()
                .and_then(|dest| dest.first()).and_then(|p| p.as_reference().ok())
                .and_then(|id| pages.get(&id).copied()).unwrap_or(0);
            out.push((depth, title, page));
            walk(doc, pages, item.get(b"First").and_then(Object::as_reference).ok(), depth + 1, out);
            next = item.get(b"Next").and_then(Object::as_reference).ok();
        }
    }
    let root = doc.catalog().unwrap().get(b"Outlines").and_then(Object::as_reference).ok();
    let first = root.and_then(|id| doc.get_dictionary(id).ok()).and_then(|r| r.get(b"First").and_then(Object::as_reference).ok());
    walk(&doc, &page_numbers, first, 0, &mut entries);
    entries
}

fn info(doc: &Document) -> Vec<(String, String)> {
    let id = doc.trailer.get(b"Info").and_then(Object::as_reference).expect("no document information");
    doc.get_dictionary(id).unwrap().iter()
        .map(|(k, v)| (String::from_utf8_lossy(k).into_owned(), format!("{v:?}")))
        .collect()
}

fn catalog_keys(doc: &Document) -> Vec<String> {
    let mut keys: Vec<String> = doc.catalog().unwrap().iter().map(|(k, _)| String::from_utf8_lossy(k).into_owned()).collect();
    keys.sort();
    keys
}

fn xmp_page_count(doc: &Document) -> Option<usize> {
    let id = doc.catalog().ok()?.get(b"Metadata").and_then(Object::as_reference).ok()?;
    let Ok(Object::Stream(stream)) = doc.get_object(id) else { return None };
    let xmp = String::from_utf8_lossy(&stream.content);
    let start = xmp.find("<xmpTPg:NPages>")? + "<xmpTPg:NPages>".len();
    xmp[start..].split('<').next()?.parse().ok()
}
