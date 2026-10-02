use image::ImageReader;
use std::io::Cursor;

use pragmatic_core::set_last_error;

/// Decode any supported format to RGBA8 raw pixels.
pub fn decode_to_rgba(data: &[u8]) -> Result<(Vec<u8>, u32, u32), String> {
    let reader = ImageReader::new(Cursor::new(data))
        .with_guessed_format()
        .map_err(|e| format!("Failed to guess format: {e}"))?;

    let img = reader
        .decode()
        .map_err(|e| format!("Failed to decode image: {e}"))?;

    let rgba = img.to_rgba8();
    let w = rgba.width();
    let h = rgba.height();
    let pixels = rgba.into_raw();

    Ok((pixels, w, h))
}

/// C ABI: decode image bytes to RGBA8.
/// Allocates output buffer via Rust. Caller must free with `pragmatic_free_buffer`.
///
/// # Safety
/// All pointers must be valid. `data` must point to `len` bytes.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn pragmatic_image_decode(
    data: *const u8,
    len: usize,
    out_rgba: *mut *mut u8,
    out_rgba_len: *mut usize,
    out_w: *mut u32,
    out_h: *mut u32,
) -> i32 {
    if data.is_null() || out_rgba.is_null() || out_rgba_len.is_null()
        || out_w.is_null() || out_h.is_null()
    {
        set_last_error("Null pointer argument");
        return -1;
    }

    let slice = unsafe { std::slice::from_raw_parts(data, len) };

    match decode_to_rgba(slice) {
        Ok((pixels, w, h)) => {
            let len = pixels.len();
            let boxed = pixels.into_boxed_slice();
            let ptr = Box::into_raw(boxed) as *mut u8;
            unsafe {
                *out_rgba = ptr;
                *out_rgba_len = len;
                *out_w = w;
                *out_h = h;
            }
            0
        }
        Err(e) => {
            set_last_error(e);
            -1
        }
    }
}
