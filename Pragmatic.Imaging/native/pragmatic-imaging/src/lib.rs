mod imaging;

use pragmatic_core::{copy_last_error, set_last_error};

// Re-export imaging C ABI functions
pub use imaging::info::pragmatic_image_info;
pub use imaging::decode::pragmatic_image_decode;
pub use imaging::encode::pragmatic_image_encode;
pub use imaging::transform::{
    pragmatic_image_resize, pragmatic_image_crop,
    pragmatic_image_rotate, pragmatic_image_flip,
};
pub use imaging::filters::{
    pragmatic_image_grayscale, pragmatic_image_blur, pragmatic_image_sharpen,
    pragmatic_image_brightness, pragmatic_image_contrast,
};
pub use imaging::qr::pragmatic_qr_generate;

/// The hash of the source this library was built from (see build.rs), as a NUL-terminated string.
/// The gate reads it out of the binary's bytes; the export is what keeps it in a stripped, LTO build.
static SOURCE_STAMP: &str = concat!(env!("PRAGMATIC_IMAGING_SOURCE_STAMP"), "\0");

/// The source stamp, `PRAGMATIC_IMAGING_SOURCE_HASH:<hex>` or `…:unstamped`.
#[unsafe(no_mangle)]
pub extern "C" fn pragmatic_imaging_source_stamp() -> *const u8 {
    SOURCE_STAMP.as_ptr()
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn pragmatic_last_error(buf: *mut u8, buf_len: usize) -> i32 {
    copy_last_error(buf, buf_len)
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn pragmatic_free_buffer(ptr: *mut u8, len: usize) {
    unsafe { pragmatic_core::free_buffer(ptr, len); }
}

/// Strip EXIF metadata from encoded image bytes.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn pragmatic_image_strip_exif(
    data: *const u8,
    len: usize,
    out_data: *mut *mut u8,
    out_len: *mut usize,
) -> i32 {
    if data.is_null() || out_data.is_null() || out_len.is_null() {
        set_last_error("Null pointer argument");
        return -1;
    }

    let slice = unsafe { std::slice::from_raw_parts(data, len) };

    match imaging::decode::decode_to_rgba(slice) {
        Ok((pixels, w, h)) => {
            let mut fmt_code = 0i32;
            let mut _w = 0u32;
            let mut _h = 0u32;
            let _ = imaging::info::image_info(slice, &mut _w, &mut _h, &mut fmt_code);

            let format = imaging::info::FormatCode::try_from(fmt_code).unwrap_or(imaging::info::FormatCode::Png);
            match imaging::encode::encode_rgba(&pixels, w, h, format, 90) {
                Ok(encoded) => {
                    let out_length = encoded.len();
                    let boxed = encoded.into_boxed_slice();
                    let ptr = Box::into_raw(boxed) as *mut u8;
                    unsafe { *out_data = ptr; *out_len = out_length; }
                    0
                }
                Err(e) => { set_last_error(e); -1 }
            }
        }
        Err(e) => { set_last_error(e); -1 }
    }
}
