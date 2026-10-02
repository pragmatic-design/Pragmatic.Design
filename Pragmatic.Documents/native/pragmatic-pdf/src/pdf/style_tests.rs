use crate::pdf::compile::compile_to_pdf;
use crate::pdf::model::DocumentModel;
use crate::pdf::typst_gen::generate_typst;

/// The JSON is shaped as C# writes it: camelCase, nulls omitted, enums as numbers.
fn markup(json: &str) -> String {
    let model: DocumentModel = serde_json::from_str(json).expect("model");
    generate_typst(&model)
}

fn render(json: &str) -> Result<Vec<u8>, String> {
    let model: DocumentModel = serde_json::from_str(json).expect("model");
    compile_to_pdf(&generate_typst(&model), &model.resources)
}

fn page(content: &str) -> String {
    format!(r#"{{"pages":[{{"content":[{content}]}}]}}"#)
}

#[test]
fn a_text_run_carries_its_font_size_weight_italic_colour_and_underline() {
    let typst = markup(&page(r##"{"$type":"paragraph","children":[{"$type":"text","content":"Total",
        "style":{"fontFamily":"Arial","fontSize":9,"fontWeight":700,"italic":true,"color":"#666","underline":true}}]}"##));

    assert!(typst.contains(
        r##"#text(font: ("Arial", "Noto Sans", "Segoe UI", "Helvetica", "Arial", "Liberation Sans", "DejaVu Sans"), size: 9pt, weight: 700, style: "italic", fill: rgb("#666"))[#underline[Total]]"##),
        "{typst}");
}

#[test]
fn a_text_without_style_is_written_as_before() {
    let typst = markup(&page(r#"{"$type":"text","content":"Plain"}"#));

    assert!(typst.contains("\nPlain\n"), "{typst}");
    assert!(!typst.contains("#text("), "{typst}");
    assert!(!typst.contains("#align("), "{typst}");
}

#[test]
fn the_default_font_is_a_list_that_ends_in_faces_every_platform_has() {
    let typst = markup(&page(r#"{"$type":"text","content":"x"}"#));

    assert!(typst.contains(
        r#"#set text(font: ("Noto Sans", "Segoe UI", "Helvetica", "Arial", "Liberation Sans", "DejaVu Sans"), size: 11pt)"#),
        "{typst}");
}

#[test]
fn a_colour_is_accepted_without_its_hash() {
    let typst = markup(&page(r#"{"$type":"text","content":"x","style":{"color":"1F4E79"}}"#));

    assert!(typst.contains(r##"fill: rgb("#1F4E79")"##), "{typst}");
}

#[test]
fn an_ooxml_highlight_name_becomes_its_colour_and_none_means_no_highlight() {
    let named = markup(&page(r#"{"$type":"text","content":"x","style":{"highlightColor":"yellow"}}"#));
    let none = markup(&page(r#"{"$type":"text","content":"x","style":{"highlightColor":"none"}}"#));

    assert!(named.contains(r##"#highlight(fill: rgb("#FFFF00"))[x]"##), "{named}");
    assert!(!none.contains("#highlight"), "{none}");
}

#[test]
fn the_alignment_of_a_text_inside_a_paragraph_is_the_paragraphs_not_its_own() {
    let typst = markup(&page(r#"{"$type":"paragraph","style":{"textAlign":2},
        "children":[{"$type":"text","content":"x","style":{"textAlign":1}}]}"#));

    assert!(typst.contains("#align(right)["), "{typst}");
    assert!(!typst.contains("#align(center)["), "{typst}");
}

#[test]
fn every_style_the_engine_maps_compiles() {
    let styled = |align: u8| format!(
        r##"{{"$type":"text","content":"Styled {align}","style":{{"fontFamily":"No Such Font 217","fontSize":14.5,
            "fontWeight":300,"italic":true,"underline":true,"strikethrough":true,"verticalPosition":{vp},
            "color":"#1F4E79","highlightColor":"darkYellow","textAlign":{align}}}}}"##,
        vp = align % 3);
    let content = [
        styled(0), styled(1), styled(2), styled(3),
        r#"{"$type":"heading","content":"Centred","level":2,"style":{"textAlign":1}}"#.to_string(),
        r#"{"$type":"paragraph","style":{"textAlign":3},"children":[{"$type":"text","content":"Justified","style":{"fontWeight":700}}]}"#.to_string(),
        r#"{"$type":"hyperlink","href":"https://example.com","style":{"textAlign":2},"children":[{"$type":"text","content":"Link","style":{"underline":true}}]}"#.to_string(),
    ].join(",");

    let pdf = render(&page(&content));

    assert!(pdf.as_ref().is_ok_and(|bytes| bytes.starts_with(b"%PDF")), "{:?}", pdf.err());
}

#[test]
fn an_invalid_colour_fails_the_render_instead_of_being_dropped() {
    let pdf = render(&page(r#"{"$type":"text","content":"x","style":{"color":"not-a-colour"}}"#));

    assert!(pdf.is_err(), "rendered with an invalid colour");
}

// A block's run formatting applies to the text inside it, and a run's own style wins where both set a
// property (PRAG-221). Until then a heading's, a paragraph's or a link's style reached the alignment only.

#[test]
fn a_headings_font_size_reaches_its_text() {
    let typst = markup(&page(r#"{"$type":"heading","content":"Invoice","level":1,"style":{"fontSize":20}}"#));

    assert!(typst.contains("= #text(size: 20pt)[Invoice]"), "{typst}");
}

#[test]
fn a_paragraphs_colour_colours_a_child_run_without_a_style() {
    let typst = markup(&page(r##"{"$type":"paragraph","style":{"color":"#C00000"},
        "children":[{"$type":"text","content":"Overdue"}]}"##));

    assert!(typst.contains(r##"#text(fill: rgb("#C00000"))[Overdue]"##), "{typst}");
}

#[test]
fn a_childs_own_colour_wins_over_the_paragraphs_and_inherits_the_rest() {
    let typst = markup(&page(r##"{"$type":"paragraph","style":{"color":"#C00000","fontSize":9},
        "children":[{"$type":"text","content":"Own","style":{"color":"#1F4E79"}},{"$type":"text","content":"Inherited"}]}"##));

    assert!(typst.contains(r##"#text(size: 9pt, fill: rgb("#1F4E79"))[Own]"##), "{typst}");
    assert!(typst.contains(r##"#text(size: 9pt, fill: rgb("#C00000"))[Inherited]"##), "{typst}");
}

#[test]
fn a_links_style_reaches_its_text() {
    let typst = markup(&page(r#"{"$type":"hyperlink","href":"https://example.com","style":{"fontWeight":700},
        "children":[{"$type":"text","content":"Pay now"}]}"#));

    assert!(typst.contains(r#"#link("https://example.com")[#text(weight: 700)[Pay now]]"#), "{typst}");
}

/// The control: a run inside an unstyled paragraph is written as before.
#[test]
fn a_run_in_an_unstyled_paragraph_is_unchanged() {
    let typst = markup(&page(r#"{"$type":"paragraph","children":[{"$type":"text","content":"Plain"}]}"#));

    assert!(typst.contains("Plain\n"), "{typst}");
    assert!(!typst.contains("#text("), "{typst}");
}

#[test]
fn a_styled_heading_compiles() {
    let pdf = render(&page(r##"{"$type":"heading","content":"Invoice","level":1,"style":{"fontSize":20,"color":"#C00000"}}"##));

    assert!(pdf.as_ref().is_ok_and(|bytes| bytes.starts_with(b"%PDF")), "{:?}", pdf.err());
}

// A heading's children are what it renders when it has any, as in DOCX (PRAG-242). The model had no
// field for them, so serde dropped `children` and the PDF wrote `content` — other words, or the same
// words without their styling.

#[test]
fn a_headings_children_are_written_instead_of_its_content() {
    let typst = markup(&page(r##"{"$type":"heading","content":"Plain","level":1,
        "children":[{"$type":"text","content":"Styled","style":{"color":"#C00000"}}]}"##));

    assert!(typst.contains(r##"= #text(fill: rgb("#C00000"))[Styled]"##), "{typst}");
    assert!(!typst.contains("Plain"), "{typst}");
}

#[test]
fn a_headings_style_reaches_a_child_without_one() {
    let typst = markup(&page(r##"{"$type":"heading","content":"Plain","level":2,"style":{"fontSize":20},
        "children":[{"$type":"text","content":"Total"},{"$type":"text","content":" due","style":{"color":"#C00000"}}]}"##));

    assert!(typst.contains(r##"== #text(size: 20pt)[Total]#text(size: 20pt, fill: rgb("#C00000"))[ due]"##), "{typst}");
}

/// The control: a heading with no children writes its content, as before.
#[test]
fn a_heading_without_children_writes_its_content() {
    let typst = markup(&page(r#"{"$type":"heading","content":"Invoice","level":1}"#));

    assert!(typst.contains("= Invoice"), "{typst}");
}

#[test]
fn a_heading_built_from_children_compiles() {
    let pdf = render(&page(r##"{"$type":"heading","content":"Plain","level":1,
        "children":[{"$type":"text","content":"Styled","style":{"color":"#C00000","fontWeight":700}}]}"##));

    assert!(pdf.as_ref().is_ok_and(|bytes| bytes.starts_with(b"%PDF")), "{:?}", pdf.err());
}
