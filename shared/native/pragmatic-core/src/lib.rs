mod error;

pub use error::{set_last_error, copy_last_error};

/// Free a buffer previously allocated by Rust.
///
/// # Safety
/// `ptr` must have been returned by a native function, with the exact `len`.
pub unsafe fn free_buffer(ptr: *mut u8, len: usize) {
    if ptr.is_null() || len == 0 {
        return;
    }
    unsafe {
        let slice = std::slice::from_raw_parts_mut(ptr, len);
        let _ = Box::from_raw(slice as *mut [u8]);
    }
}
