//! Windows 命名互斥量：保护跨进程的状态更新和连接操作。
use std::{io, marker::PhantomData, path::Path, rc::Rc, time::Duration};
use windows::core::PCWSTR;
use windows::Win32::Foundation::{
    CloseHandle, HANDLE, WAIT_ABANDONED, WAIT_OBJECT_0, WAIT_TIMEOUT,
};
use windows::Win32::System::Threading::{CreateMutexW, ReleaseMutex, WaitForSingleObject};

// Mutex 必须由获得它的线程释放，禁止把 guard 发送到其他线程。
pub struct ProcessLock {
    handle: HANDLE,
    _thread: PhantomData<Rc<()>>,
}

impl ProcessLock {
    pub fn acquire(path: &Path, timeout: Duration) -> io::Result<Self> {
        // 用数据目录区分用户和测试沙箱，同一配置在不同进程/会话使用同一个锁。
        let mut hash = 0xcbf29ce484222325u64;
        for b in path.to_string_lossy().to_lowercase().bytes() {
            hash = (hash ^ b as u64).wrapping_mul(0x100000001b3);
        }
        let name: Vec<u16> = format!("Global\\SNNU-AutoLogin-{hash:016x}")
            .encode_utf16()
            .chain(Some(0))
            .collect();
        unsafe {
            let handle = CreateMutexW(None, false, PCWSTR(name.as_ptr()))
                .map_err(|e| io::Error::other(e.to_string()))?;
            let result =
                WaitForSingleObject(handle, timeout.as_millis().min(u32::MAX as u128 - 1) as u32);
            if result == WAIT_OBJECT_0 || result == WAIT_ABANDONED {
                Ok(Self {
                    handle,
                    _thread: PhantomData,
                })
            } else {
                let error = if result == WAIT_TIMEOUT {
                    io::Error::new(io::ErrorKind::TimedOut, "等待程序锁超时")
                } else {
                    io::Error::last_os_error()
                };
                let _ = CloseHandle(handle);
                Err(error)
            }
        }
    }
}

impl Drop for ProcessLock {
    fn drop(&mut self) {
        unsafe {
            let _ = ReleaseMutex(self.handle);
            let _ = CloseHandle(self.handle);
        }
    }
}
