// No real credentials, startup entries or campus network requests in these tests.
namespace Microsoft.UI.Dispatching
{
    public sealed class DispatcherQueue
    {
        public bool TryEnqueue(Action action) { action(); return true; }
    }
}

namespace SNNUAutoLogin.Services
{
    public sealed class AppSettings
    {
        public bool AutoStart { get; set; } = true;
        public bool AutoReconnect { get; set; }
        public string Account { get; set; } = "test";
        public int Operator { get; set; }
        public int CheckIntervalSeconds { get; set; } = 8;
    }

    public sealed class CredentialStore
    {
        public AppSettings Settings { get; } = new();
        public string Password { get; set; } = "test-password";
        public string LoadPassword() => Password;
        public bool HasPassword => Password.Length > 0;
    }

    public sealed class LoginResult
    {
        public bool Success { get; init; }
        public bool AlreadyOnline { get; init; }
        public string Message { get; init; } = "";
        public string? Account { get; init; }
        public string? Ip { get; init; }
        public static LoginResult Fail(string message) => new() { Message = message };
    }

    public sealed class PortalClient : IDisposable
    {
        public static LoginResult Status { get; set; } = LoginResult.Fail("offline");
        public static LoginResult Login { get; set; } = new() { Success = true };
        public static int LoginCalls { get; set; }
        public Task<LoginResult> GetStatusAsync(CancellationToken ct) => Task.FromResult(Status);
        public Task<LoginResult> LoginAsync(string account, string password, int op,
            CancellationToken ct = default)
        {
            LoginCalls++;
            return Task.FromResult(Login);
        }
        public void Dispose() { }
    }
}
