use serde::{Deserialize, Deserializer};

/// Rust mirror of Pragmatic.Documents.Model.DocumentModel (C# → JSON → Rust).
/// Only the fields needed for PDF rendering.
#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DocumentModel {
    pub title: Option<String>,
    pub author: Option<String>,
    pub language: Option<String>,
    #[serde(default)]
    pub page_size: PageSize,
    #[serde(default)]
    pub orientation: PageOrientation,
    #[serde(default)]
    pub margins: Margins,
    #[serde(default)]
    pub pages: Vec<DocumentPage>,
    /// Named resources (image name → base64 PNG bytes). Passed from C# PdfRenderer.
    #[serde(default)]
    pub resources: std::collections::HashMap<String, String>,
}

#[derive(Debug, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct DocumentPage {
    #[serde(default)]
    pub header: Option<Vec<DocumentNode>>,
    #[serde(default)]
    pub footer: Option<Vec<DocumentNode>>,
    #[serde(default)]
    pub content: Vec<DocumentNode>,
}

#[derive(Debug, Deserialize)]
#[serde(tag = "$type", rename_all = "camelCase")]
pub enum DocumentNode {
    #[serde(rename = "text")]
    Text { content: String, #[serde(default)] style: Option<NodeStyle> },
    #[serde(rename = "heading")]
    Heading {
        content: String,
        #[serde(default = "default_level")] level: u8,
        // Rendered instead of `content` when present, as HeadingNode documents and DOCX does. Without the
        // field serde dropped them and the PDF wrote `content` (PRAG-242).
        #[serde(default)] children: Vec<DocumentNode>,
        #[serde(default)] style: Option<NodeStyle>,
    },
    #[serde(rename = "paragraph")]
    Paragraph { #[serde(default)] children: Vec<DocumentNode>, #[serde(default)] style: Option<NodeStyle> },
    #[serde(rename = "image")]
    Image { source: String, alt: Option<String>, width: Option<f64>, height: Option<f64>, #[serde(default)] style: Option<NodeStyle> },
    #[serde(rename = "table")]
    Table {
        #[serde(default)] columns: Vec<TableColumn>,
        header: Option<TableRow>,
        #[serde(default)] rows: Vec<TableRow>,
        #[serde(default = "default_true")] repeat_header: bool,
    },
    #[serde(rename = "list")]
    List { #[serde(default)] ordered: bool, #[serde(default)] items: Vec<ListItem> },
    #[serde(rename = "hr")]
    HorizontalRule { #[serde(default = "default_thickness")] thickness: f64 },
    #[serde(rename = "spacer")]
    Spacer { #[serde(default = "default_spacer_height")] height: f64 },
    #[serde(rename = "container")]
    Container { #[serde(default)] children: Vec<DocumentNode> },
    #[serde(rename = "pagebreak")]
    PageBreak,
    #[serde(rename = "barcode")]
    Barcode { value: String, #[serde(default)] r#type: BarcodeType, width: Option<f64>, height: Option<f64> },
    #[serde(rename = "hyperlink")]
    Hyperlink { href: String, #[serde(default)] children: Vec<DocumentNode>, #[serde(default)] style: Option<NodeStyle> },
    #[serde(rename = "toc")]
    Toc { #[serde(default = "default_toc_level", rename = "maxLevel")] max_level: u8, title: Option<String> },
    #[serde(rename = "footnote")]
    Footnote { content: String },
    #[serde(rename = "field")]
    Field { #[serde(rename = "fieldType")] field_type: FieldType, format: Option<String> },
    #[serde(rename = "bookmark")]
    Bookmark { name: String, #[serde(default)] children: Vec<DocumentNode> },
}

/// The part of Pragmatic.Documents.Model.NodeStyle the PDF engine applies, as the DOCX renderer applies
/// it: run formatting on the text, cascading from a heading, a paragraph or a link to the text inside it
/// (`style::inherit`), and alignment on a block. Spacing, borders, background, sizes and letter spacing
/// are not mapped; serde drops them as unknown fields.
#[derive(Debug, Deserialize, Default, Clone)]
#[serde(rename_all = "camelCase")]
pub struct NodeStyle {
    pub font_family: Option<String>,
    /// Points.
    pub font_size: Option<f64>,
    pub font_weight: Option<FontWeight>,
    pub italic: Option<bool>,
    pub underline: Option<bool>,
    pub strikethrough: Option<bool>,
    pub vertical_position: Option<VerticalPosition>,
    pub color: Option<String>,
    pub highlight_color: Option<String>,
    pub text_align: Option<TextAlign>,
}

/// The CSS weight number, which is what the C# enum's values are (Light 300, Normal 400, Bold 700).
#[derive(Debug, Clone, Copy)]
pub struct FontWeight(pub u16);

impl<'de> Deserialize<'de> for FontWeight {
    fn deserialize<D: Deserializer<'de>>(d: D) -> Result<Self, D::Error> {
        let v = serde_json::Value::deserialize(d)?;
        Ok(match &v {
            serde_json::Value::Number(n) => Self(n.as_u64().map_or(400, |w| w.clamp(100, 900) as u16)),
            serde_json::Value::String(s) => match s.as_str() {
                "Bold" | "bold" => Self(700), "Light" | "light" => Self(300), _ => Self(400),
            },
            _ => Self(400),
        })
    }
}

#[derive(Debug, Default, Clone, Copy, PartialEq)]
pub enum VerticalPosition { #[default] None, Superscript, Subscript }

impl<'de> Deserialize<'de> for VerticalPosition {
    fn deserialize<D: Deserializer<'de>>(d: D) -> Result<Self, D::Error> {
        let v = serde_json::Value::deserialize(d)?;
        Ok(match &v {
            serde_json::Value::Number(n) => match n.as_u64().unwrap_or(0) {
                1 => Self::Superscript, 2 => Self::Subscript, _ => Self::None,
            },
            serde_json::Value::String(s) => match s.as_str() {
                "Superscript" | "superscript" => Self::Superscript,
                "Subscript" | "subscript" => Self::Subscript,
                _ => Self::None,
            },
            _ => Self::None,
        })
    }
}

#[derive(Debug, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct TableColumn {
    pub width: Option<f64>,
    pub align: Option<TextAlign>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct TableRow {
    #[serde(default)]
    pub cells: Vec<TableCell>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct TableCell {
    #[serde(default)]
    pub content: Vec<DocumentNode>,
    #[serde(default = "default_one")]
    pub col_span: u32,
    #[serde(default = "default_one")]
    pub row_span: u32,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ListItem {
    #[serde(default)]
    pub content: Vec<DocumentNode>,
}

/// C# enums serialize as integers by default in source-gen JSON context.
/// Accept both integer and string.
#[derive(Debug, Default, Clone, Copy)]
pub enum PageSize { #[default] A4, A3, A5, Letter, Legal, Custom }

impl<'de> Deserialize<'de> for PageSize {
    fn deserialize<D: Deserializer<'de>>(d: D) -> Result<Self, D::Error> {
        let v = serde_json::Value::deserialize(d)?;
        Ok(match &v {
            serde_json::Value::Number(n) => match n.as_u64().unwrap_or(0) {
                1 => Self::A3, 2 => Self::A5, 3 => Self::Letter, 4 => Self::Legal, 5 => Self::Custom, _ => Self::A4,
            },
            serde_json::Value::String(s) => match s.as_str() {
                "A3" => Self::A3, "A5" => Self::A5, "Letter" => Self::Letter, "Legal" => Self::Legal, _ => Self::A4,
            },
            _ => Self::A4,
        })
    }
}

#[derive(Debug, Default, Clone, Copy)]
pub enum PageOrientation { #[default] Portrait, Landscape }

impl<'de> Deserialize<'de> for PageOrientation {
    fn deserialize<D: Deserializer<'de>>(d: D) -> Result<Self, D::Error> {
        let v = serde_json::Value::deserialize(d)?;
        Ok(match &v {
            serde_json::Value::Number(n) if n.as_u64() == Some(1) => Self::Landscape,
            serde_json::Value::String(s) if s == "Landscape" || s == "landscape" => Self::Landscape,
            _ => Self::Portrait,
        })
    }
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Margins {
    #[serde(default = "default_margin")]
    pub top: f64,
    #[serde(default = "default_margin")]
    pub right: f64,
    #[serde(default = "default_margin")]
    pub bottom: f64,
    #[serde(default = "default_margin")]
    pub left: f64,
}

impl Default for Margins {
    fn default() -> Self {
        Self { top: 25.0, right: 25.0, bottom: 25.0, left: 25.0 }
    }
}

#[derive(Debug, Default, Clone, Copy)]
pub enum TextAlign { #[default] Left, Center, Right, Justify }

impl<'de> Deserialize<'de> for TextAlign {
    fn deserialize<D: Deserializer<'de>>(d: D) -> Result<Self, D::Error> {
        let v = serde_json::Value::deserialize(d)?;
        Ok(match &v {
            serde_json::Value::Number(n) => match n.as_u64().unwrap_or(0) {
                1 => Self::Center, 2 => Self::Right, 3 => Self::Justify, _ => Self::Left,
            },
            serde_json::Value::String(s) => match s.as_str() {
                "Center" | "center" => Self::Center, "Right" | "right" => Self::Right,
                "Justify" | "justify" => Self::Justify, _ => Self::Left,
            },
            _ => Self::Left,
        })
    }
}

#[derive(Debug, Default, Clone, Copy)]
pub enum BarcodeType { #[default] QrCode, Code128, Code39, Ean13, Ean8 }

impl<'de> Deserialize<'de> for BarcodeType {
    fn deserialize<D: Deserializer<'de>>(d: D) -> Result<Self, D::Error> {
        let v = serde_json::Value::deserialize(d)?;
        Ok(match &v {
            serde_json::Value::Number(n) => match n.as_u64().unwrap_or(0) {
                1 => Self::Code128, 2 => Self::Code39, 3 => Self::Ean13, 4 => Self::Ean8, _ => Self::QrCode,
            },
            serde_json::Value::String(s) => match s.as_str() {
                "Code128" => Self::Code128, "Code39" => Self::Code39,
                "Ean13" => Self::Ean13, "Ean8" => Self::Ean8, _ => Self::QrCode,
            },
            _ => Self::QrCode,
        })
    }
}

fn default_toc_level() -> u8 { 3 }
fn default_level() -> u8 { 1 }
fn default_true() -> bool { true }
fn default_thickness() -> f64 { 0.5 }
fn default_spacer_height() -> f64 { 10.0 }
fn default_one() -> u32 { 1 }
fn default_margin() -> f64 { 25.0 }

#[derive(Debug, Default, Clone, Copy)]
pub enum FieldType { #[default] Page, NumPages, Date, Time, FileName }

impl<'de> Deserialize<'de> for FieldType {
    fn deserialize<D: Deserializer<'de>>(d: D) -> Result<Self, D::Error> {
        let v = serde_json::Value::deserialize(d)?;
        Ok(match &v {
            serde_json::Value::Number(n) => match n.as_u64().unwrap_or(0) {
                1 => Self::NumPages, 2 => Self::Date, 3 => Self::Time, 4 => Self::FileName, _ => Self::Page,
            },
            serde_json::Value::String(s) => match s.as_str() {
                "NumPages" | "numPages" => Self::NumPages,
                "Date" | "date" => Self::Date,
                "Time" | "time" => Self::Time,
                "FileName" | "fileName" => Self::FileName,
                _ => Self::Page,
            },
            _ => Self::Page,
        })
    }
}
