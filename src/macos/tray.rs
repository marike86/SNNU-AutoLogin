//! The worker publishes data only; AppKit is exclusively accessed by the main thread.
pub use crate::tray_model::Link;
use std::sync::atomic::AtomicBool;
use std::sync::{
    atomic::{AtomicU8, Ordering},
    Arc, Mutex, OnceLock,
};
static LINK: AtomicU8 = AtomicU8::new(0);
static NOTICE: OnceLock<Mutex<crate::notification::Latest>> = OnceLock::new();
fn notices() -> &'static Mutex<crate::notification::Latest> {
    NOTICE.get_or_init(|| Mutex::new(crate::notification::Latest::default()))
}
fn now() -> u64 {
    (crate::portal::unix_now() * 1000.) as u64
}
pub fn link() -> Link {
    Link::from_byte(LINK.load(Ordering::Relaxed))
}
pub fn set_link(link: Link) {
    LINK.store(link as u8, Ordering::Relaxed);
}
pub fn start(_: Arc<AtomicBool>) {}
pub fn refresh_tip() {}
pub fn balloon(title: &str, text: &str) {
    notices()
        .lock()
        .unwrap()
        .publish(crate::notification::Kind::Info, title, text, now());
}
pub fn connecting(title: &str, text: &str) {
    notices()
        .lock()
        .unwrap()
        .publish(crate::notification::Kind::Connecting, title, text, now());
}
pub fn connected(text: &str) {
    notices().lock().unwrap().publish(
        crate::notification::Kind::Connected,
        "连接已恢复",
        text,
        now(),
    );
}
pub fn failed(text: &str) {
    notices()
        .lock()
        .unwrap()
        .publish(crate::notification::Kind::Failed, "连接未成功", text, now());
}
pub fn take_notice() -> Option<crate::notification::Notice> {
    notices().lock().unwrap().take(now())
}
pub fn expire_notice() -> bool {
    notices().lock().unwrap().expire(now())
}
