use image::ImageFormat;
use image::ImageReader;
use std::io::Cursor;

use pragmatic_core::set_last_error;

/// Image format codes matching the C# `ImageFormat` enum.
#[repr(i32)]
#[derive(Debug, Clone, Copy)]
pub enum FormatCode {
    Unknown = 0,
    Png = 1,
    Jpeg = 2,
    WebP = 3,
    Avif = 4,
    Gif = 5,
    Bmp = 6,
    Tiff = 7,
}

impl From<ImageFormat> for FormatCode {
    fn from(f: ImageFormat) -> Self {
        match f {
            ImageFormat::Png => FormatCode::Png,
            ImageFormat::Jpeg => FormatCode::Jpeg,
            ImageFormat::WebP => FormatCode::WebP,
            ImageFormat::Avif => FormatCode::Avif,
            ImageFormat::Gif => FormatCode::Gif,
            ImageFormat::Bmp => FormatCode::Bmp,
            ImageFormat::Tiff => FormatCode::Tiff,
            _ => FormatCode::Unknown,
        }
    }
}

impl TryFrom<i32> for FormatCode {
    type Error = String;
    fn try_from(value: i32) -> Result<Self, Self::Error> {
        match value {
            0 => Ok(FormatCode::Unknown),
            1 => Ok(FormatCode::Png),
            2 => Ok(FormatCode::Jpeg),
            3 => Ok(FormatCode::WebP),
            4 => Ok(FormatCode::Avif),
            5 => Ok(FormatCode::Gif),
            6 => Ok(FormatCode::Bmp),
            7 => Ok(FormatCode::Tiff),
            _ => Err(format!("Invalid image format: {value}. Valid: 0-7.")),
        }
    }
}

impl FormatCode {
    #[allow(dead_code)]
    pub fn to_image_format(self) -> Option<ImageFormat> {
        match self {
            FormatCode::Png => Some(ImageFormat::Png),
            FormatCode::Jpeg => Some(ImageFormat::Jpeg),
            FormatCode::WebP => Some(ImageFormat::WebP),
            FormatCode::Avif => Some(ImageFormat::Avif),
            FormatCode::Gif => Some(ImageFormat::Gif),
            FormatCode::Bmp => Some(ImageFormat::Bmp),
            FormatCode::Tiff => Some(ImageFormat::Tiff),
            FormatCode::Unknown => None,
        }
    }
}

/// Get image dimensions and format without full decode.
pub fn image_info(
    data: &[u8],
    out_w: &mut u32,
    out_h: &mut u32,
    out_format: &mut i32,
) -> Result<(), String> {
    let reader = ImageReader::new(Cursor::new(data))
        .with_guessed_format()
        .map_err(|e| format!("Failed to guess format: {e}"))?;

    let format = reader.format().unwrap_or(ImageFormat::Png);
    *out_format = FormatCode::from(format) as i32;

    let (w, h) = reader
        .into_dimensions()
        .map_err(|e| format!("Failed to read dimensions: {e}"))?;

    *out_w = w;
    *out_h = h;
    Ok(())
}

/// C ABI: get image info without full decode.
/// Returns 0 on success, -1 on error.
///
/// # Safety
/// `data` must point to `len` valid bytes. Output pointers must be non-null.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn pragmatic_image_info(
    data: *const u8,
    len: usize,
    out_w: *mut u32,
    out_h: *mut u32,
    out_format: *mut i32,
) -> i32 {
    if data.is_null() || out_w.is_null() || out_h.is_null() || out_format.is_null() {
        set_last_error("Null pointer argument");
        return -1;
    }
    let slice = unsafe { std::slice::from_raw_parts(data, len) };
    let mut w = 0u32;
    let mut h = 0u32;
    let mut fmt = 0i32;

    match image_info(slice, &mut w, &mut h, &mut fmt) {
        Ok(()) => {
            unsafe {
                *out_w = w;
                *out_h = h;
                *out_format = fmt;
            }
            0
        }
        Err(e) => {
            set_last_error(e);
            -1
        }
    }
}
