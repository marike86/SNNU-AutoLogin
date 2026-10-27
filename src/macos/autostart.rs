//! macOS 13+ login item, with status always read back from ServiceManagement.
use objc2::runtime::AnyObject;
use objc2::{class, msg_send};
#[link(name = "ServiceManagement", kind = "framework")]
extern "C" {}
pub fn enabled() -> bool {
    status() == 1
}
pub fn registered() -> bool {
    matches!(status(), 1 | 2)
}
pub fn status() -> isize {
    unsafe {
        let service: *mut AnyObject = msg_send![class!(SMAppService), mainAppService];
        msg_send![service, status]
    }
}
pub fn set_enabled(enabled: bool) -> Result<(), String> {
    unsafe {
        let service: *mut AnyObject = msg_send![class!(SMAppService), mainAppService];
        let mut error: *mut AnyObject = std::ptr::null_mut();
        let ok: bool = if enabled {
            msg_send![service, registerAndReturnError: &mut error]
        } else {
            msg_send![service, unregisterAndReturnError: &mut error]
        };
        if !ok {
            return Err(
                "无法更改登录启动设置，请将应用移至应用程序文件夹，并检查系统设置中的登录项".into(),
            );
        }
        if enabled && !self::enabled() {
            return Err("请在系统设置 → 通用 → 登录项中允许此应用运行".into());
        }
        if !enabled && registered() {
            return Err("未能关闭登录自启动，请在系统登录项中检查后重试".into());
        }
        Ok(())
    }
}
