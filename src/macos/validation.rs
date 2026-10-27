//! Explicit opt-in fixture commands. They refuse the real profile or non-loopback portals.
use crate::{
    agent, config,
    macos::{runtime, storage},
    portal,
};
use std::{path::PathBuf, time::Duration};
fn isolated() -> PathBuf {
    let dir = std::env::var_os("SNNU_DATA_DIR").expect("fixture requires SNNU_DATA_DIR");
    assert_eq!(
        std::env::var("SNNU_PORTAL_HOST").as_deref(),
        Ok("127.0.0.1")
    );
    assert!(std::env::var("SNNU_KEYCHAIN_SERVICE")
        .unwrap()
        .starts_with("io.github.shixiang-liu.SNNU-AutoLogin.test."));
    PathBuf::from(dir)
}
pub fn setup() {
    isolated();
    let cfg = config::Config {
        account: "20241234".into(),
        operator: "mobile".into(),
        ..Default::default()
    };
    config::save_config(&cfg, "fixture-password").expect("fixture Keychain save");
    storage::retry_keychain();
    assert_eq!(config::load_config().1, "fixture-password");
    let raw = std::fs::read_to_string(config::cfg_path()).unwrap();
    assert!(!raw.contains("fixture-password"));
    config::try_update_state(|s| {
        s.port = 8601;
        s.portal_host = "127.0.0.1".into();
        s.reconnect_paused = false;
        s.paused_until = 0.;
    })
    .unwrap();
}
fn fixture(mode: &str) {
    ureq::post("http://127.0.0.1:8601/__fixture")
        .send_string(mode)
        .unwrap();
    config::try_update_state(|s| {
        s.reconnect_paused = false;
        s.paused_until = 0.;
        s.port = 8601;
        s.portal_host = "127.0.0.1".into();
    })
    .unwrap();
}
pub fn run() {
    let dir = isolated();
    // The online macOS runner must expose its underlying physical Ethernet
    // interface even when portal traffic itself is an isolated loopback fixture.
    assert!(
        crate::network::physical_source().is_some(),
        "no routed physical IPv4 interface detected on the online runner"
    );
    setup();
    fixture("offline");
    assert_eq!(
        portal::status(Duration::from_secs(2)).0,
        portal::Status::Offline
    );
    portal::login_automatic("20241234", "fixture-password", "mobile", 8601, || false).unwrap();
    let online = portal::status(Duration::from_secs(2)).0;
    assert!(matches!(&online, portal::Status::Online(s) if s.account == "20241234@mobile"));
    assert!(config::load_state().self_login_ok);
    assert!(portal::logout(8601));
    assert_eq!(
        portal::status(Duration::from_secs(2)).0,
        portal::Status::Offline
    );
    fixture("credential");
    let error = portal::login_automatic("20241234", "bad", "mobile", 8601, || false).unwrap_err();
    assert_eq!(portal::fail_kind(&error), portal::FailKind::Credential);
    agent::record_login_failure(&error);
    assert!(config::load_state().paused_until > portal::unix_now());
    fixture("account-state");
    let error = portal::login_automatic("20241234", "fixture-password", "mobile", 8601, || false)
        .unwrap_err();
    assert_eq!(portal::fail_kind(&error), portal::FailKind::AccountState);
    fixture("offline");
    config::try_update_state(|s| s.reconnect_paused = true).unwrap();
    assert_eq!(
        portal::login_automatic("20241234", "fixture-password", "mobile", 8601, || false)
            .unwrap_err(),
        "自动登录已暂停"
    );
    fixture("online-other");
    portal::login_automatic("20241234", "fixture-password", "mobile", 8601, || false).unwrap();
    assert!(
        matches!(portal::status(Duration::from_secs(2)).0, portal::Status::Online(s) if s.account == "20249999@mobile")
    );
    let stats: serde_json::Value = serde_json::from_str(
        &ureq::get("http://127.0.0.1:8601/__fixture")
            .call()
            .unwrap()
            .into_string()
            .unwrap(),
    )
    .unwrap();
    assert_eq!(stats["posts"], 0);
    config::try_update_state(|s| s.reconnect_paused = true).unwrap();
    runtime::refresh_status();
    let deadline = std::time::Instant::now() + Duration::from_secs(3);
    while !matches!(runtime::status(), portal::Status::Online(s) if s.account == "20249999@mobile")
    {
        assert!(std::time::Instant::now() < deadline);
        std::thread::sleep(Duration::from_millis(10));
    }
    assert!(config::load_state().reconnect_paused);
    let stats: serde_json::Value = serde_json::from_str(
        &ureq::get("http://127.0.0.1:8601/__fixture")
            .call()
            .unwrap()
            .into_string()
            .unwrap(),
    )
    .unwrap();
    assert_eq!(stats["posts"], 0);
    // Saving new credentials must never silently replace a session that appeared
    // after the user opened the form, or overwrite a good saved password on failure.
    assert!(super::app::save_and_connect("20241234", "fixture-password", "mobile", false).is_err());
    assert!(
        matches!(portal::status(Duration::from_secs(2)).0, portal::Status::Online(s) if s.account == "20249999@mobile")
    );
    fixture("credential");
    assert!(super::app::save_and_connect("20245555", "bad", "mobile", false).is_err());
    storage::retry_keychain();
    assert_eq!(config::load_config().0.account, "20241234");
    assert_eq!(config::load_config().1, "fixture-password");
    fixture("offline");
    super::app::save_and_connect("20241234", "fixture-password", "mobile", false).unwrap();
    // A failed config rename must not replace an existing password or leave a
    // newly created Keychain item behind. Obstruct only this isolated config.
    let (saved, _) = config::load_config();
    let backup = dir.join("config-before-failure.json");
    std::fs::rename(config::cfg_path(), &backup).unwrap();
    std::fs::create_dir(config::cfg_path()).unwrap();
    assert!(config::save_config(&saved, "replacement-password").is_err());
    let service = std::env::var("SNNU_KEYCHAIN_SERVICE").unwrap();
    assert_eq!(
        security_framework::passwords::get_generic_password(&service, "20241234")
            .unwrap()
            .as_slice(),
        b"fixture-password"
    );
    let new_account = config::Config {
        account: "20245555".into(),
        ..saved.clone()
    };
    assert!(config::save_config(&new_account, "replacement-password").is_err());
    assert!(security_framework::passwords::get_generic_password(&service, "20245555").is_err());
    std::fs::remove_dir(config::cfg_path()).unwrap();
    std::fs::rename(backup, config::cfg_path()).unwrap();
    storage::retry_keychain();
    assert_eq!(config::load_config().1, "fixture-password");
    let mut existing = saved;
    existing.keepalive = false;
    existing.keepalive_min = 1;
    config::save_config(&existing, "fixture-password").unwrap();
    fixture("offline");
    super::app::save_and_connect("20241234", "fixture-password", "mobile", false).unwrap();
    assert!(!config::load_config().0.keepalive);
    assert_eq!(config::load_config().0.keepalive_min, 5);
    fixture("offline");
    assert!(
        portal::login_automatic("20241234", "fixture-password", "mobile", 8601, || true).is_err()
    );
    runtime::publish(online);
    let diag = super::app::diagnostic();
    assert!(!diag.contains("fixture-password"));
    assert!(!diag.contains("20241234"));
    storage::delete_account().unwrap();
    storage::retry_keychain();
    assert!(config::load_config().0.account.is_empty());
    assert!(config::load_config().1.is_empty());
    let service = std::env::var("SNNU_KEYCHAIN_SERVICE").unwrap();
    assert!(security_framework::passwords::get_generic_password(&service, "20241234").is_err());
    fixture("offline");
    super::app::save_and_connect("20241234", "fixture-password", "mobile", false).unwrap();
    assert!(config::load_config().0.keepalive);
    assert_eq!(config::load_config().0.keepalive_min, 15);
    storage::delete_account().unwrap();
    std::fs::write(dir.join("integration.json"), serde_json::to_vec_pretty(&serde_json::json!({
        "login":true,"logout":true,"credential_failure":true,"account_restriction":true,
        "pause_guard":true,"other_account_preserved":true,"cancel_guard":true,
        "keychain_roundtrip":true,"keychain_delete":true,"no_plaintext_config":true,"redacted_diagnostic":true,
        "save_validates_before_replacing_credentials":true,"save_rechecks_online_session":true,
        "config_write_failure_restores_keychain":true,"failed_save_leaves_no_orphan_secret":true,
        "first_save_enables_keepalive":true,"existing_keepalive_preference_preserved":true,
        "keepalive_interval_normalized":true,
        "passive_refresh_does_not_resume_authentication":true
        ,"physical_interface_detected":true
    })).unwrap()).unwrap();
}
