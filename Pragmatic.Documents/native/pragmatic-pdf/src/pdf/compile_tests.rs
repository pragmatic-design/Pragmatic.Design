use crate::pdf::chunked::{render_with, CELL_BUDGET};
use crate::pdf::compile::CachedFonts;
use crate::pdf::model::DocumentModel;

/// A Linux image without fontconfig: the engine sees no font at all. The render used to succeed with
/// no font embedded and no error.
#[test]
fn a_machine_with_no_font_fails_the_render_and_says_what_to_install() {
    let model: DocumentModel = serde_json::from_str(
        r#"{"pages":[{"content":[{"$type":"text","content":"Invoice total"}]}]}"#).unwrap();

    let result = render_with(&model, &CachedFonts::none(), CELL_BUDGET);

    let error = result.expect_err("rendered a PDF with no font to set its text in");
    assert!(error.contains("No fonts found"), "{error}");
    assert!(error.contains("fontconfig"), "{error}");
}
