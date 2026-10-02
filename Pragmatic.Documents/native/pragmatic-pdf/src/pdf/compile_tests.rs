use std::collections::HashMap;
use crate::pdf::compile::{compile_with_fonts, CachedFonts};

/// A Linux image without fontconfig: the engine sees no font at all. The render used to succeed with
/// no font embedded and no error.
#[test]
fn a_machine_with_no_font_fails_the_render_and_says_what_to_install() {
    let result = compile_with_fonts("Invoice total", &HashMap::new(), &CachedFonts::none());

    let error = result.expect_err("rendered a PDF with no font to set its text in");
    assert!(error.contains("No fonts found"), "{error}");
    assert!(error.contains("fontconfig"), "{error}");
}
