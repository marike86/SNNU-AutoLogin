using Microsoft.UI.Dispatching;
using Microsoft.Win32;
using SNNUAutoLogin.Services;

int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine("PASS " + name);
    checks++;
}

// Exercise Windows registry operations in a disposable key, never the real Run key.
string runKey = @"Software\SNNUAutoLogin.Tests\" + Guid.NewGuid().ToString("N");
string path = @"C:\Portable App\SNNUAutoLogin.exe";
if (!args.Contains("--skip-registry"))
{
try
{
    Check(StartupManager.SetEnabled(false, path, runKey), "disable absent key");
    Check(StartupManager.SetEnabled(true, path, runKey), "create missing key");
    using (var key = Registry.CurrentUser.OpenSubKey(runKey))
        Check((string?)key?.GetValue("SNNUAutoLogin") == $"\"{path}\" --tray", "quoted path and tray argument");
    string moved = @"C:\Moved App\SNNUAutoLogin.exe";
    Check(!StartupManager.IsEnabled(moved, runKey), "same filename at old path is invalid");
    Check(StartupManager.SetEnabled(true, moved, runKey), "repair moved portable path");
    Check(!StartupManager.IsEnabled(path, runKey), "old path no longer matches");
    using (var key = Registry.CurrentUser.OpenSubKey(runKey, true))
        key!.SetValue("SNNUAutoLogin", $"\"{moved}\"");
    Check(!StartupManager.IsEnabled(moved, runKey), "missing tray argument is invalid");
    Check(StartupManager.SetEnabled(true, moved, runKey), "repair legacy command");
    Check(!StartupManager.SetEnabled(true, "", runKey), "empty path rejected");
    Check(StartupManager.SetEnabled(false, moved, runKey), "disable removes entry");
}
finally { Registry.CurrentUser.DeleteSubKeyTree(runKey, false); }
}
else Console.WriteLine("SKIP registry integration (restricted environment)");

async Task Scenario(string name, bool startup, Action<CredentialStore>? configure,
    LoginResult status, LoginResult login, params int[] expectedCalls)
{
    var store = new CredentialStore();
    configure?.Invoke(store);
    PortalClient.Status = status;
    PortalClient.Login = login;
    PortalClient.LoginCalls = 0;
    using var coordinator = new LoginCoordinator(store, new DispatcherQueue(), startup);
    foreach (int expected in expectedCalls)
    {
        await coordinator.RefreshAsync();
        Check(PortalClient.LoginCalls == expected, name + " tick " + checks);
    }
}

var offline = LoginResult.Fail("offline");
var ok = new LoginResult { Success = true };
await Scenario("startup logs in once with reconnect off", true, null, offline, ok, 1, 1);
await Scenario("manual launch respects reconnect off", false, null, offline, ok, 0);
await Scenario("disabled startup does not log in", true, s => s.Settings.AutoStart = false, offline, ok, 0);
await Scenario("reconnect remains active", true, s => s.Settings.AutoReconnect = true, offline, ok, 1, 2);
await Scenario("failed startup retries", true, null, offline, LoginResult.Fail("rejected"), 1, 2);
await Scenario("missing password", true, s => s.Password = "", offline, ok, 0);
await Scenario("missing account", true, s => s.Settings.Account = " ", offline, ok, 0);

var pendingStore = new CredentialStore();
using (var coordinator = new LoginCoordinator(pendingStore, new DispatcherQueue(), true))
{
    PortalClient.LoginCalls = 0;
    PortalClient.Status = LoginResult.Fail("Portal unreachable");
    await coordinator.RefreshAsync();
    Check(PortalClient.LoginCalls == 0, "wait for network without submitting credentials");
    PortalClient.Status = offline;
    PortalClient.Login = ok;
    coordinator.SuppressAuto(true);
    await coordinator.RefreshAsync();
    Check(PortalClient.LoginCalls == 0, "suppression applies to startup login");
    coordinator.SuppressAuto(false);
    await coordinator.RefreshAsync();
    Check(PortalClient.LoginCalls == 1, "startup login survives network delay");
    await coordinator.RefreshAsync();
    Check(PortalClient.LoginCalls == 1, "no reconnect after startup success");
}

using (var coordinator = new LoginCoordinator(new CredentialStore(), new DispatcherQueue(), true))
{
    PortalClient.LoginCalls = 0;
    PortalClient.Status = ok;
    await coordinator.RefreshAsync();
    PortalClient.Status = offline;
    await coordinator.RefreshAsync();
    Check(PortalClient.LoginCalls == 0, "already online completes startup login");
}
Console.WriteLine($"{checks} checks passed.");
