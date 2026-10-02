use typst::foundations::{Bytes, Datetime};
use typst::text::Font;
use typst::utils::LazyHash;
use typst::text::FontBook;
use typst_library::{Library, World};
use typst_library::layout::PagedDocument;
use typst::syntax::{FileId, Source, VirtualPath};
use typst::diag::FileResult;
use typst_kit::fonts::{FontSlot, Fonts};

use std::collections::HashMap;
use std::sync::OnceLock;
use base64::Engine;

/// Cached font database — searched once, reused across all renders.
pub(crate) struct CachedFonts {
    book: LazyHash<FontBook>,
    fonts: Vec<FontSlot>,
}

impl CachedFonts {
    /// What a machine with no font the engine can see looks like.
    #[cfg(test)]
    pub(crate) fn none() -> Self {
        Self { book: LazyHash::new(FontBook::new()), fonts: Vec::new() }
    }
}

static FONT_CACHE: OnceLock<CachedFonts> = OnceLock::new();

fn get_fonts() -> &'static CachedFonts {
    FONT_CACHE.get_or_init(|| {
        let result = Fonts::searcher().search();
        CachedFonts {
            book: LazyHash::new(result.book),
            fonts: result.fonts,
        }
    })
}

/// Compile Typst markup to PDF bytes, with optional image resources.
pub fn compile_to_pdf(typst_markup: &str, resources: &HashMap<String, String>) -> Result<Vec<u8>, String> {
    compile_with_fonts(typst_markup, resources, get_fonts())
}

pub(crate) fn compile_with_fonts(typst_markup: &str, resources: &HashMap<String, String>, fonts: &CachedFonts) -> Result<Vec<u8>, String> {
    // Typst compiles a document with no font at all without an error, and the PDF embeds no font:
    // the render "succeeds" with text that has no face to be drawn in. On Linux that is any image
    // without fontconfig, because the font search reads only the folders /etc/fonts/fonts.conf lists.
    if fonts.fonts.is_empty() {
        return Err("No fonts found on this machine. On Linux the engine reads fontconfig's configuration: \
                    install fontconfig and a font package (e.g. fonts-dejavu-core).".to_string());
    }

    let world = PragmaticWorld::new(typst_markup, resources, fonts);

    // Compile Typst source to a paged document
    let warned = typst::compile::<PagedDocument>(&world);

    let document = warned.output.map_err(|errors| {
        let msgs: Vec<String> = errors.iter()
            .map(|e| e.message.to_string())
            .collect();
        format!("Typst compilation failed: {}", msgs.join("; "))
    })?;

    // Render paged document to PDF bytes
    let options = typst_pdf::PdfOptions::default();
    let pdf_bytes = typst_pdf::pdf(&document, &options)
        .map_err(|errors| {
            let msgs: Vec<String> = errors.iter()
                .map(|e| e.message.to_string())
                .collect();
            format!("PDF generation failed: {}", msgs.join("; "))
        })?;

    Ok(pdf_bytes)
}

/// Minimal Typst World implementation with virtual file support for image resources.
/// Font database is cached globally via FONT_CACHE (searched once per process).
struct PragmaticWorld<'a> {
    library: LazyHash<Library>,
    source: Source,
    /// Virtual files: name.png → decoded bytes
    virtual_files: HashMap<String, Vec<u8>>,
    fonts: &'a CachedFonts,
}

impl<'a> PragmaticWorld<'a> {
    fn new(markup: &str, resources: &HashMap<String, String>, fonts: &'a CachedFonts) -> Self {
        let source = Source::new(
            FileId::new(None, VirtualPath::new("main.typ")),
            markup.to_string(),
        );

        // Decode base64 resources into virtual files
        let mut virtual_files = HashMap::new();
        for (name, b64) in resources {
            if let Ok(bytes) = base64::engine::general_purpose::STANDARD.decode(b64) {
                virtual_files.insert(format!("{name}.png"), bytes);
            }
        }

        Self {
            library: LazyHash::new(Library::default()),
            source,
            virtual_files,
            fonts,
        }
    }
}

impl World for PragmaticWorld<'_> {
    fn library(&self) -> &LazyHash<Library> {
        &self.library
    }

    fn book(&self) -> &LazyHash<FontBook> {
        &self.fonts.book
    }

    fn main(&self) -> FileId {
        self.source.id()
    }

    fn source(&self, id: FileId) -> FileResult<Source> {
        if id == self.source.id() {
            Ok(self.source.clone())
        } else {
            Err(typst::diag::FileError::NotFound(id.vpath().as_rooted_path().into()))
        }
    }

    fn file(&self, id: FileId) -> FileResult<Bytes> {
        // Check virtual files (resources)
        let path = id.vpath().as_rooted_path();
        let name = path.to_string_lossy();
        // Strip leading path separators (/ on Unix, \ on Windows)
        let name = name.trim_start_matches('/').trim_start_matches('\\');

        if let Some(bytes) = self.virtual_files.get(name) {
            return Ok(Bytes::new(bytes.clone()));
        }

        Err(typst::diag::FileError::NotFound(path.into()))
    }

    fn font(&self, index: usize) -> Option<Font> {
        self.fonts.fonts.get(index).and_then(|slot| slot.get())
    }

    fn today(&self, offset: Option<i64>) -> Option<Datetime> {
        let now = std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .ok()?;
        let offset_secs = offset.unwrap_or(0) * 3600;
        let total_secs = now.as_secs() as i64 + offset_secs;
        let days = total_secs.div_euclid(86400);
        let z = days + 719468;
        let era = z.div_euclid(146097);
        let doe = (z - era * 146097) as u64;
        let yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365;
        let y = yoe as i64 + era * 400;
        let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
        let mp = (5 * doy + 2) / 153;
        let d = doy - (153 * mp + 2) / 5 + 1;
        let m = if mp < 10 { mp + 3 } else { mp - 9 };
        let y = if m <= 2 { y + 1 } else { y };
        Datetime::from_ymd(y as i32, m as u8, d as u8)
    }
}
