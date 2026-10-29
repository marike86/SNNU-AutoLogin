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
        public bool NeedCaptcha { get; init; }
        public string Message { get; init; } = "";
        public string? Account { get; init; }
        public string? Ip { get; init; }
        public static LoginResult Fail(string message) => new() { Message = message };
        public static LoginResult Captcha(string message) => new() { NeedCaptcha = true, Message = message };
    }

    /// <summary>图形验证码图片；测试里只用来占位，不会真的渲染。</summary>
    public sealed record CaptchaImage(byte[] Bytes, string Hint);

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
        public Task<LoginResult> LoginAsync(string account, string password, int op, string captcha)
        {
            LoginCalls++;
            return Task.FromResult(Login);
        }
        public Task<CaptchaImage?> FetchCaptchaAsync(CancellationToken ct = default) =>
            Task.FromResult<CaptchaImage?>(null);
        public void Dispose() { }
    }
}
