using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SNNUAutoLogin.Services;

public sealed class AppSettings
{
    public string Account { get; set; } = "";
    public Operator Operator { get; set; } = Operator.Mobile;
    public bool AutoStart { get; set; } = true;
    public bool AutoReconnect { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public int CheckIntervalSeconds { get; set; } = 8;
}

/// <summary>设置与凭据持久化。密码通过 DPAPI（当前用户作用域）加密落盘，不存明文。</summary>
public sealed class CredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SNNU.AutoLogin.v1");

    private readonly string Dir;

    private string SettingsPath => Path.Combine(Dir, "settings.json");
    private string SecretPath => Path.Combine(Dir, "secret.bin");

    public CredentialStore() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SNNUAutoLogin")) { }

    internal CredentialStore(string directory) => Dir = directory;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // 巡检每几秒就要读一次密码，DPAPI 解密 + 读盘没必要重复做，解密结果缓存在内存里
    private string? _pwdCache;
    private bool _pwdLoaded;
    private bool _passwordDirty;
    private string _lastJson = "";

    public AppSettings Settings { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (Directory.Exists(Dir) is false) Directory.CreateDirectory(Dir);
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath, Encoding.UTF8);
                var s = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts);
                if (s != null) Settings = s;
                Settings.Account ??= "";
                if (!Enum.IsDefined(Settings.Operator)) Settings.Operator = Operator.Mobile;
                if (Settings.CheckIntervalSeconds is < 4 or > 120) Settings.CheckIntervalSeconds = 8;
                _lastJson = json;
            }
        }
        catch
        {
            Settings = new AppSettings();
        }

        _pwdCache = null;
        _pwdLoaded = false;
        _passwordDirty = false;
    }

    public bool Save()
    {
        try
        {
            if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);

            var json = JsonSerializer.Serialize(Settings, JsonOpts);
            if (json == _lastJson) return true;   // 内容没变就不落盘

            WriteAtomically(SettingsPath, Encoding.UTF8.GetBytes(json));
            _lastJson = json;
            return true;
        }
        catch { return false; }
    }

    public bool SavePassword(string? password)
    {
        password ??= "";

        // 与缓存一致说明没改过，跳过 DPAPI 加密与写盘（点「登录」时不再重复落盘）
        if (_pwdLoaded && !_passwordDirty && string.Equals(_pwdCache, password, StringComparison.Ordinal)) return true;

        _pwdCache = password;
        _pwdLoaded = true;
        _passwordDirty = true;

        try
        {
            if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);

            if (password.Length == 0)
            {
                File.Delete(SecretPath);
                _passwordDirty = false;
                return true;
            }

            var data = Encoding.UTF8.GetBytes(password);
            try
            {
                var cipher = ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
                WriteAtomically(SecretPath, cipher);
                _passwordDirty = false;
                return true;
            }
            finally { CryptographicOperations.ZeroMemory(data); }
        }
        catch { return false; }
    }

    /// <summary>读取密码（首次解密后缓存，后续调用为纯内存读取）。</summary>
    public string LoadPassword()
    {
        if (_pwdLoaded) return _pwdCache ?? "";

        _pwdLoaded = true;
        _pwdCache = "";
        try
        {
            if (!File.Exists(SecretPath)) return _pwdCache;

            var cipher = File.ReadAllBytes(SecretPath);
            if (cipher.Length == 0) return _pwdCache;

            var data = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
            _pwdCache = Encoding.UTF8.GetString(data);
            CryptographicOperations.ZeroMemory(data);
        }
        catch
        {
            _pwdCache = "";
        }

        return _pwdCache;
    }

    public bool HasPassword => LoadPassword().Length > 0;

    private static void WriteAtomically(string path, byte[] data)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, data);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
