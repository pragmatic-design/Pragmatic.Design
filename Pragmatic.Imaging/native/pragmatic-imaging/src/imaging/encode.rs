use image::{ImageBuffer, ImageFormat, Rgba, codecs, DynamicImage};
use std::io::Cursor;

use pragmatic_core::set_last_error;
use crate::imaging::info::FormatCode;

/// Encode RGBA8 raw pixels to the specified format.
/// `quality` is used for JPEG (1-100). WebP and AVIF use library defaults (image 0.25 does not expose quality control for these).
/// Lossless formats (PNG, BMP, TIFF, GIF) ignore quality.
/// For JPEG: auto-converts RGBA→RGB (JPEG doesn't support alpha).
pub fn encode_rgba(
    rgba: &[u8],
    w: u32,
    h: u32,
    format: FormatCode,
    quality: u8,
) -> Result<Vec<u8>, String> {
    let img: ImageBuffer<Rgba<u8>, _> = ImageBuffer::from_raw(w, h, rgba.to_vec())
        .ok_or_else(|| "Invalid RGBA buffer dimensions".to_string())?;

    let dynamic = DynamicImage::ImageRgba8(img);
    let mut buf = Cursor::new(Vec::new());

    match format {
        FormatCode::Png => {
            dynamic.write_to(&mut buf, ImageFormat::Png)
                .map_err(|e| format!("PNG encode failed: {e}"))?;
        }
        FormatCode::Jpeg => {
            // JPEG doesn't support alpha — convert to RGB first
            let rgb = dynamic.to_rgb8();
            let encoder = codecs::jpeg::JpegEncoder::new_with_quality(&mut buf, quality);
            rgb.write_with_encoder(encoder)
                .map_err(|e| format!("JPEG encode failed: {e}"))?;
        }
        FormatCode::WebP => {
            // image 0.25 WebPEncoder only supports lossless; write_to uses default lossy
            // Quality control requires a dedicated WebP library (future improvement)
            dynamic.write_to(&mut buf, ImageFormat::WebP)
                .map_err(|e| format!("WebP encode failed: {e}"))?;
        }
        FormatCode::Gif => {
            dynamic.write_to(&mut buf, ImageFormat::Gif)
                .map_err(|e| format!("GIF encode failed: {e}"))?;
        }
        FormatCode::Bmp => {
            dynamic.write_to(&mut buf, ImageFormat::Bmp)
                .map_err(|e| format!("BMP encode failed: {e}"))?;
        }
        FormatCode::Tiff => {
            dynamic.write_to(&mut buf, ImageFormat::Tiff)
                .map_err(|e| format!("TIFF encode failed: {e}"))?;
        }
        FormatCode::Avif => {
            dynamic.write_to(&mut buf, ImageFormat::Avif)
                .map_err(|e| format!("AVIF encode failed: {e}"))?;
        }
        FormatCode::Unknown => {
            return Err("Cannot encode to unknown format".to_string());
        }
    }

    Ok(buf.into_inner())
}

/// C ABI: encode RGBA8 pixels to the specified format.
/// Allocates output buffer via Rust. Caller must free with `pragmatic_free_buffer`.
///
/// # Safety
/// `rgba` must point to `len` valid bytes. Output pointers must be non-null.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn pragmatic_image_encode(
    rgba: *const u8,
    len: usize,
    w: u32,
    h: u32,
    format: i32,
    quality: u8,
    out_data: *mut *mut u8,
    out_len: *mut usize,
) -> i32 {
    if rgba.is_null() || out_data.is_null() || out_len.is_null() {
        set_last_error("Null pointer argument");
        return -1;
    }

    let slice = unsafe { std::slice::from_raw_parts(rgba, len) };
    let fmt = match FormatCode::try_from(format) {
        Ok(f) => f,
        Err(e) => { set_last_error(e); return -1; }
    };

    match encode_rgba(slice, w, h, fmt, quality) {
        Ok(encoded) => {
            let out_length = encoded.len();
            let boxed = encoded.into_boxed_slice();
            let ptr = Box::into_raw(boxed) as *mut u8;
            unsafe {
                *out_data = ptr;
                *out_len = out_length;
            }
            0
        }
        Err(e) => {
            set_last_error(e);
            -1
        }
    }
}
