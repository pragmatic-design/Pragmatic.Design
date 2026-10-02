use std::cell::RefCell;

thread_local! {
    static LAST_ERROR: RefCell<String> = RefCell::new(String::new());
}

/// Store an error message in thread-local storage.
pub fn set_last_error(msg: impl Into<String>) {
    LAST_ERROR.with(|e| *e.borrow_mut() = msg.into());
}

/// Copy the last error into the caller-provided buffer.
/// Returns the number of bytes written (excluding NUL), or -1 if the buffer is too small.
pub fn copy_last_error(buf: *mut u8, buf_len: usize) -> i32 {
    LAST_ERROR.with(|e| {
        let err = e.borrow();
        if err.is_empty() {
            if buf_len > 0 && !buf.is_null() {
                unsafe { *buf = 0; }
            }
            return 0;
        }
        let bytes = err.as_bytes();
        // Need space for the string + NUL terminator
        if buf_len < bytes.len() + 1 || buf.is_null() {
            return -(bytes.len() as i32 + 1);
        }
        unsafe {
            std::ptr::copy_nonoverlapping(bytes.as_ptr(), buf, bytes.len());
            *buf.add(bytes.len()) = 0; // NUL terminator
        }
        bytes.len() as i32
    })
}
