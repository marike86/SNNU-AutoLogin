//! 状态通知只保留最新一条，不在应用里排队播放过时的连接状态。
#[derive(Clone, Copy, Debug, PartialEq)]
pub enum Kind {
    Connecting,
    Connected,
    Failed,
    Info,
}

impl Kind {
    fn lifetime_ms(self) -> u64 {
        match self {
            Self::Connecting | Self::Failed => 8_000,
            _ => 5_000,
        }
    }
}

#[derive(Debug)]
pub struct Notice {
    pub kind: Kind,
    pub title: String,
    pub text: String,
    ready_at: u64,
    expires_at: u64,
}

#[derive(Default)]
pub struct Latest {
    pending: Option<Notice>,
    active_until: Option<u64>,
}

impl Latest {
    pub fn publish(&mut self, kind: Kind, title: &str, text: &str, now: u64) {
        self.pending = Some(Notice {
            kind,
            title: title.into(),
            text: text.into(),
            ready_at: now.saturating_add(300),
            expires_at: now.saturating_add(kind.lifetime_ms()),
        });
    }

    pub fn take(&mut self, now: u64) -> Option<Notice> {
        // 先撤回旧气泡，给系统的关闭动画留出极短时间；期间仍只保留最新状态。
        if self.pending.as_ref().is_some_and(|n| now < n.ready_at) {
            return None;
        }
        let notice = self.pending.take()?;
        if now >= notice.expires_at {
            return None;
        }
        self.active_until = Some(now.saturating_add(notice.kind.lifetime_ms()));
        Some(notice)
    }

    pub fn expire(&mut self, now: u64) -> bool {
        if self.active_until.is_some_and(|until| now >= until) {
            self.active_until = None;
            true
        } else {
            false
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn success_supersedes_pending_progress_instead_of_queuing() {
        let mut latest = Latest::default();
        latest.publish(Kind::Connecting, "正在连接", "", 0);
        latest.publish(Kind::Connected, "连接成功", "", 1200);
        assert!(latest.take(1201).is_none());
        assert_eq!(latest.take(1500).unwrap().kind, Kind::Connected);
        assert!(latest.take(1501).is_none());
    }
    #[test]
    fn expired_notice_is_never_delivered_late() {
        let mut latest = Latest::default();
        latest.publish(Kind::Connecting, "正在连接", "", 0);
        assert!(latest.take(30_000).is_none());
    }
    #[test]
    fn old_progress_deadline_cannot_dismiss_new_result() {
        let mut latest = Latest::default();
        latest.publish(Kind::Connecting, "正在连接", "", 0);
        assert!(latest.take(300).is_some());
        latest.publish(Kind::Connected, "已连接", "", 7000);
        assert!(latest.take(7300).is_some());
        assert!(!latest.expire(8300));
        assert!(latest.expire(12300));
        assert!(!latest.expire(12301));
    }
}
