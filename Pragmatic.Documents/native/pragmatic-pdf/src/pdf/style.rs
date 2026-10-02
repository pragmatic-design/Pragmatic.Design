use std::fmt::Write;
use crate::pdf::model::{NodeStyle, TextAlign, VerticalPosition};
use crate::pdf::typst_gen::escape_str;

/// Tried in order. Noto Sans is the preference; the others are the sans faces Windows, macOS and
/// Linux installs carry. Without a list, a machine missing the one named font gets whatever font
/// Typst finds first that covers the glyphs — on Windows that is Segoe UI Emoji.
const DEFAULT_FONTS: &[&str] = &["Noto Sans", "Segoe UI", "Helvetica", "Arial", "Liberation Sans", "DejaVu Sans"];

/// A Typst font array: `family` first when given, then the default list, so a family the machine
/// does not have falls back to a text face rather than to the first font that covers the glyphs.
pub fn font_list(family: Option<&str>) -> String {
    let names: Vec<String> = family.into_iter()
        .chain(DEFAULT_FONTS.iter().copied())
        .map(|name| format!("\"{}\"", escape_str(name)))
        .collect();
    format!("({})", names.join(", "))
}

/// The run formatting a text run is written with: its own, and the enclosing block's where the run sets
/// nothing — property by property, as in CSS. A heading, a paragraph and a link are blocks; alignment is
/// the paragraph's and is taken from the run as before, which `write_run` does not read anyway (PRAG-221).
pub fn inherit(block: Option<&NodeStyle>, run: Option<&NodeStyle>) -> Option<NodeStyle> {
    match (block, run) {
        (None, None) => None,
        (Some(block), None) => Some(block.clone()),
        (None, Some(run)) => Some(run.clone()),
        (Some(block), Some(run)) => Some(NodeStyle {
            font_family: run.font_family.clone().or_else(|| block.font_family.clone()),
            font_size: run.font_size.or(block.font_size),
            font_weight: run.font_weight.or(block.font_weight),
            italic: run.italic.or(block.italic),
            underline: run.underline.or(block.underline),
            strikethrough: run.strikethrough.or(block.strikethrough),
            vertical_position: run.vertical_position.or(block.vertical_position),
            color: run.color.clone().or_else(|| block.color.clone()),
            highlight_color: run.highlight_color.clone().or_else(|| block.highlight_color.clone()),
            text_align: run.text_align,
        }),
    }
}

/// Writes `body` inside the run formatting of `style` — font, size, weight, italic, colour, then
/// underline, strike-through, super/subscript and highlight. Without run formatting, `body` alone.
pub fn write_run(out: &mut String, style: Option<&NodeStyle>, body: impl FnOnce(&mut String)) {
    let Some(style) = style else {
        body(out);
        return;
    };

    let mut open = 0;
    let args = text_args(style);
    if !args.is_empty() {
        write!(out, "#text({})[", args.join(", ")).unwrap();
        open += 1;
    }
    if style.underline == Some(true) {
        out.push_str("#underline[");
        open += 1;
    }
    if style.strikethrough == Some(true) {
        out.push_str("#strike[");
        open += 1;
    }
    match style.vertical_position {
        Some(VerticalPosition::Superscript) => { out.push_str("#super["); open += 1; }
        Some(VerticalPosition::Subscript) => { out.push_str("#sub["); open += 1; }
        _ => {}
    }
    if let Some(fill) = style.highlight_color.as_deref().and_then(highlight_fill) {
        write!(out, "#highlight(fill: {fill})[").unwrap();
        open += 1;
    }

    body(out);

    for _ in 0..open {
        out.push(']');
    }
}

/// Writes `body` as a block aligned by `style`'s text alignment. Without one, `body` alone.
pub fn write_aligned(out: &mut String, style: Option<&NodeStyle>, body: impl FnOnce(&mut String)) {
    let open = match style.and_then(|s| s.text_align) {
        None => {
            body(out);
            return;
        }
        Some(TextAlign::Left) => "#align(left)[",
        Some(TextAlign::Center) => "#align(center)[",
        Some(TextAlign::Right) => "#align(right)[",
        Some(TextAlign::Justify) => "#block[#set par(justify: true)",
    };
    writeln!(out, "{open}").unwrap();
    body(out);
    writeln!(out, "]").unwrap();
}

fn text_args(style: &NodeStyle) -> Vec<String> {
    let mut args = Vec::new();
    if let Some(family) = style.font_family.as_deref().map(str::trim).filter(|f| !f.is_empty()) {
        args.push(format!("font: {}", font_list(Some(family))));
    }
    if let Some(size) = style.font_size.filter(|s| s.is_finite() && *s > 0.0) {
        args.push(format!("size: {size}pt"));
    }
    if let Some(weight) = style.font_weight {
        args.push(format!("weight: {}", weight.0));
    }
    if style.italic == Some(true) {
        args.push("style: \"italic\"".to_string());
    }
    if let Some(color) = style.color.as_deref() {
        args.push(format!("fill: {}", hex_color(color)));
    }
    args
}

/// The value as written, with or without its `#`. An invalid one is Typst's to reject, and the
/// render fails naming it, rather than the colour being dropped.
fn hex_color(value: &str) -> String {
    format!("rgb(\"#{}\")", escape_str(value.trim().trim_start_matches('#')))
}

/// The highlight is either one of OOXML's highlight names, which is what DOCX writes, or a hex value.
fn highlight_fill(value: &str) -> Option<String> {
    let value = value.trim();
    if value.eq_ignore_ascii_case("none") {
        return None;
    }
    let named = match value.to_ascii_lowercase().as_str() {
        "black" => Some("000000"), "blue" => Some("0000FF"), "cyan" => Some("00FFFF"),
        "green" => Some("00FF00"), "magenta" => Some("FF00FF"), "red" => Some("FF0000"),
        "yellow" => Some("FFFF00"), "white" => Some("FFFFFF"), "darkblue" => Some("000080"),
        "darkcyan" => Some("008080"), "darkgreen" => Some("008000"), "darkmagenta" => Some("800080"),
        "darkred" => Some("800000"), "darkyellow" => Some("808000"), "darkgray" => Some("808080"),
        "lightgray" => Some("C0C0C0"),
        _ => None,
    };
    Some(hex_color(named.unwrap_or(value)))
}
