//! In-process worker and an interruptible wait: no 200 ms polling on macOS.
use std::sync::{
    atomic::{AtomicBool, Ordering},
    Arc, Condvar, Mutex, OnceLock,
};
use std::time::Duration;
static STARTED: AtomicBool = AtomicBool::new(false);
static SIGNALS: OnceLock<(Arc<AtomicBool>, Arc<AtomicBool>)> = OnceLock::new();
static WAIT: (Mutex<()>, Condvar) = (Mutex::new(()), Condvar::new());
static STATUS: Mutex<crate::portal::Status> = Mutex::new(crate::portal::Status::Unreachable);
static PROBING: AtomicBool = AtomicBool::new(false);
pub static BUSY: AtomicBool = AtomicBool::new(false);
pub static OPEN_REQUESTED: AtomicBool = AtomicBool::new(false);
pub static EPOCH: crate::ui_model::RefreshEpoch = crate::ui_model::RefreshEpoch::new();
pub fn signals() -> (Arc<AtomicBool>, Arc<AtomicBool>) {
    SIGNALS
        .get_or_init(|| {
            (
                Arc::new(AtomicBool::new(false)),
                Arc::new(AtomicBool::new(false)),
            )
        })
        .clone()
}
pub fn start() {
    if !STARTED.swap(true, Ordering::SeqCst) {
        std::thread::Builder::new()
            .name("snnu-agent".into())
            .spawn(crate::agent::run_daemon)
            .expect("后台线程启动失败");
    }
}
pub fn wake() {
    let _guard = WAIT.0.lock().unwrap();
    signals().1.store(true, Ordering::SeqCst);
    WAIT.1.notify_all();
}
pub fn stop() {
    signals().0.store(true, Ordering::SeqCst);
    wake();
}
pub fn quitting() -> bool {
    signals().0.load(Ordering::SeqCst)
}
pub fn wait(duration: Duration, quit: &AtomicBool, wake: &AtomicBool) {
    let lock = WAIT.0.lock().unwrap();
    let _result = WAIT
        .1
        .wait_timeout_while(lock, duration, |_| {
            !quit.load(Ordering::SeqCst) && !wake.load(Ordering::SeqCst)
        })
        .unwrap();
}
pub fn publish(status: crate::portal::Status) {
    crate::tray::set_link(match &status {
        crate::portal::Status::Online(_) => crate::tray::Link::Online,
        crate::portal::Status::Offline => crate::tray::Link::Offline,
        crate::portal::Status::Unreachable => crate::tray::Link::Waiting,
    });
    *STATUS.lock().unwrap() = status;
}
/// Passive observation must continue while a window is open even if automatic
/// authentication is paused. Never submit credentials or change the pause flag.
pub fn refresh_status() {
    if BUSY.load(Ordering::SeqCst) || PROBING.swap(true, Ordering::SeqCst) {
        return;
    }
    let stamp = EPOCH.current();
    std::thread::spawn(move || {
        let status = crate::portal::status(Duration::from_secs(4)).0;
        publish_probe(status, stamp);
        PROBING.store(false, Ordering::SeqCst);
    });
}
pub fn publish_probe(status: crate::portal::Status, stamp: u64) {
    if EPOCH.accepts(stamp, BUSY.load(Ordering::SeqCst)) {
        publish(status);
    }
}
pub fn status() -> crate::portal::Status {
    STATUS.lock().unwrap().clone()
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn network_or_resume_event_interrupts_a_long_wait() {
        let quit = AtomicBool::new(false);
        let wake_flag = AtomicBool::new(false);
        std::thread::scope(|scope| {
            scope.spawn(|| {
                std::thread::sleep(Duration::from_millis(25));
                let _guard = WAIT.0.lock().unwrap();
                wake_flag.store(true, Ordering::SeqCst);
                WAIT.1.notify_all();
            });
            let start = std::time::Instant::now();
            wait(Duration::from_secs(30), &quit, &wake_flag);
            assert!(start.elapsed() < Duration::from_secs(2));
            assert!(wake_flag.load(Ordering::SeqCst));
        });
    }
}
