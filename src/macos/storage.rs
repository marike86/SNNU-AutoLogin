//! Passwords live only in Keychain. A locked/denied Keychain never erases a saved item.
use crate::config::{Config, APP_NAME};
use security_framework::passwords::{
    delete_generic_password, get_generic_password, set_generic_password,
};
use std::{
    io,
    path::PathBuf,
    sync::{Mutex, OnceLock},
};
const SERVICE: &str = "io.github.shixiang-liu.SNNU-AutoLogin";
static CACHE: OnceLock<Mutex<Option<(String, String)>>> = OnceLock::new();
static ERROR: Mutex<Option<String>> = Mutex::new(None);
fn cache() -> &'static Mutex<Option<(String, String)>> {
    CACHE.get_or_init(|| Mutex::new(None))
}
pub fn app_dir() -> PathBuf {
    // Explicit isolation for CI; never infer fixtures from APPDATA or modify the real profile.
    if let Some(dir) = std::env::var_os("SNNU_DATA_DIR") {
        return PathBuf::from(dir);
    }
    PathBuf::from(std::env::var_os("HOME").expect("macOS user home unavailable"))
        .join("Library/Application Support")
        .join(APP_NAME)
}
fn service() -> String {
    match std::env::var("SNNU_KEYCHAIN_SERVICE") {
        Ok(value)
            if std::env::var_os("SNNU_DATA_DIR").is_some()
                && value.starts_with("io.github.shixiang-liu.SNNU-AutoLogin.test.") =>
        {
            value
        }
        _ => SERVICE.into(),
    }
}
fn read_config() -> Config {
    let raw = std::fs::read_to_string(crate::config::cfg_path()).unwrap_or_default();
    let mut cfg: Config = serde_json::from_str(&raw).unwrap_or_default();
    cfg.password_enc.clear();
    if cfg.operator == "telecom" {
        cfg.operator.clear();
    }
    cfg
}
pub fn last_error() -> Option<String> {
    ERROR.lock().unwrap().clone()
}
pub fn retry_keychain() {
    *cache().lock().unwrap() = None;
    *ERROR.lock().unwrap() = None;
}
fn secret(account: &str) -> io::Result<Option<Vec<u8>>> {
    match get_generic_password(&service(), account) {
        Ok(bytes) => Ok(Some(bytes.to_vec())),
        Err(error) if error.code() == -25300 => Ok(None),
        Err(error) => Err(io::Error::other(format!(
            "无法访问钥匙串（{}）",
            error.code()
        ))),
    }
}
fn restore_secret(account: &str, previous: Option<&[u8]>) -> io::Result<()> {
    let result = match previous {
        Some(bytes) => set_generic_password(&service(), account, bytes),
        None => delete_generic_password(&service(), account),
    };
    match result {
        Ok(()) => Ok(()),
        Err(error) if previous.is_none() && error.code() == -25300 => Ok(()),
        Err(error) => Err(io::Error::other(format!(
            "钥匙串回退失败（{}）",
            error.code()
        ))),
    }
}
pub fn load_config() -> (Config, String) {
    let cfg = read_config();
    if cfg.account.is_empty() {
        return (cfg, String::new());
    }
    let mut saved = cache().lock().unwrap();
    if let Some((account, password)) = &*saved {
        if *account == cfg.account {
            return (cfg, password.clone());
        }
    }
    // Avoid repeatedly prompting from a background worker after access was refused.
    if ERROR.lock().unwrap().is_some() {
        return (cfg, String::new());
    }
    let password = match get_generic_password(&service(), &cfg.account) {
        Ok(bytes) => match String::from_utf8(bytes.to_vec()) {
            Ok(password) => password,
            Err(_) => {
                *ERROR.lock().unwrap() = Some("钥匙串中的密码格式无效，请重新保存账号".into());
                String::new()
            }
        },
        Err(error) if error.code() == -25300 => String::new(),
        Err(error) => {
            *ERROR.lock().unwrap() = Some(format!(
                "无法读取钥匙串，请解锁或允许访问后重试（{}）",
                error.code()
            ));
            String::new()
        }
    };
    if ERROR.lock().unwrap().is_none() {
        *saved = Some((cfg.account.clone(), password.clone()));
    }
    (cfg, password)
}
pub fn save_config(cfg: &Config, password: &str) -> io::Result<()> {
    if cfg.account.trim().is_empty() || password.is_empty() {
        return Err(io::Error::other("请填写账号和密码"));
    }
    // Only serialize non-secret fields. Windows DPAPI tokens are never imported.
    let mut c = cfg.clone();
    c.password_enc.clear();
    let json = serde_json::to_string_pretty(&c)?;
    let old = read_config();
    let previous = secret(&cfg.account)?;
    set_generic_password(&service(), &cfg.account, password.as_bytes())
        .map_err(|e| io::Error::other(format!("密码未保存：钥匙串访问失败（{}）", e.code())))?;
    if let Err(error) = crate::config::atomic_write(&crate::config::cfg_path(), &json) {
        restore_secret(&cfg.account, previous.as_deref())
            .map_err(|rollback| io::Error::other(format!("配置保存失败：{error}；{rollback}")))?;
        return Err(error);
    }
    *cache().lock().unwrap() = Some((cfg.account.clone(), password.into()));
    *ERROR.lock().unwrap() = None;
    if !old.account.is_empty() && old.account != cfg.account {
        if let Err(e) = delete_generic_password(&service(), &old.account) {
            if e.code() != -25300 {
                crate::config::logf("账号已更新，旧钥匙串项目未能删除");
            }
        }
    }
    Ok(())
}
pub fn delete_account() -> io::Result<()> {
    let cfg = read_config();
    crate::agent::pause_automatic(true).map_err(io::Error::other)?;
    // Match desktop deletion: stop launching automatically before forgetting
    // credentials. Fixtures must never change the user's real login items.
    if std::env::var_os("SNNU_DATA_DIR").is_none() && crate::autostart::registered() {
        crate::autostart::set_enabled(false).map_err(io::Error::other)?;
    }
    // Delete the secret first; on failure retain the account so the action can be retried.
    let previous = if cfg.account.is_empty() {
        None
    } else {
        secret(&cfg.account)?
    };
    if !cfg.account.is_empty() {
        if let Err(e) = delete_generic_password(&service(), &cfg.account) {
            if e.code() != -25300 {
                return Err(io::Error::other(format!(
                    "无法删除钥匙串密码（{}）",
                    e.code()
                )));
            }
        }
    }
    if let Err(error) = crate::config::atomic_write(&crate::config::cfg_path(), "{}") {
        if !cfg.account.is_empty() {
            restore_secret(&cfg.account, previous.as_deref()).map_err(|rollback| {
                io::Error::other(format!("账号删除失败：{error}；{rollback}"))
            })?;
        }
        return Err(error);
    }
    retry_keychain();
    Ok(())
}
