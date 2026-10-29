using SNNUAutoLogin.Services;

var root = Path.Combine(AppContext.BaseDirectory, "SNNU-CredentialTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    checks++;
    Console.WriteLine("PASS " + name);
}
try
{
    var store = new CredentialStore(root);
    store.Load();
    store.Settings.Account = "test-account";
    Check(store.Save(), "settings saved");
    Check(store.SavePassword("test-password"), "DPAPI password saved");
    var reloaded = new CredentialStore(root);
    reloaded.Load();
    Check(reloaded.Settings.Account == "test-account", "account restored");
    Check(reloaded.LoadPassword() == "test-password", "password restored after restart");

    string secret = Path.Combine(root, "secret.bin");
    File.Delete(secret);
    Directory.CreateDirectory(secret); // deterministic write failure
    Check(!store.SavePassword("new-password"), "password write failure reported");
    Directory.Delete(secret);
    Check(store.SavePassword("new-password"), "same password retried after failure");
    reloaded.Load();
    Check(reloaded.LoadPassword() == "new-password", "retry survives restart");

    File.Delete(secret);
    Directory.CreateDirectory(secret);
    Check(!store.SavePassword(""), "password deletion failure reported");
    Directory.Delete(secret);
    Check(store.SavePassword(""), "password deletion retry succeeds");

    string settings = Path.Combine(root, "settings.json");
    File.Delete(settings);
    Directory.CreateDirectory(settings);
    store.Settings.Account = "updated";
    Check(!store.Save(), "settings failure reported");
    Directory.Delete(settings);
    Check(store.Save(), "unchanged settings retry succeeds");

    File.WriteAllText(settings, "{\"Account\":null,\"Operator\":999,\"CheckIntervalSeconds\":-1}");
    reloaded.Load();
    Check(reloaded.Settings.Account == "", "null account normalized");
    Check(reloaded.Settings.Operator == Operator.Mobile, "invalid operator normalized");
    Check(reloaded.Settings.CheckIntervalSeconds == 8, "invalid interval normalized");
    File.WriteAllText(settings, "{broken");
    reloaded.Load();
    Check(reloaded.Settings.Account == "", "corrupt settings recover defaults");
    Console.WriteLine($"{checks} credential checks passed.");
}
finally { Directory.Delete(root, true); }

namespace SNNUAutoLogin.Services
{
    public enum Operator { Campus, Mobile, Unicom, Telecom }
}
