using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SNNUAutoLogin.Services;

/// <summary>运营商出口。Campus 表示不选择运营商（校内直连/教育网）。</summary>
public enum Operator
{
    Campus = 0,
    Mobile = 1,
    Unicom = 2,
    Telecom = 3
}

public sealed class LoginResult
{
    public bool Success { get; init; }
    public bool AlreadyOnline { get; init; }
    public string Message { get; init; } = "";
    public string? Account { get; init; }
    public string? Ip { get; init; }
    public string? LoginTime { get; init; }

    public static LoginResult Ok(string msg, string? acc = null, string? ip = null, string? time = null)
        => new() { Success = true, Message = msg, Account = acc, Ip = ip, LoginTime = time };

    public static LoginResult Online(string? acc, string? ip, string? time)
        => new() { Success = true, AlreadyOnline = true, Message = "已在线", Account = acc, Ip = ip, LoginTime = time };

    public static LoginResult Fail(string msg) => new() { Success = false, Message = msg };
}

/// <summary>
/// 陕西师范大学校园网 Portal 客户端。
/// 协议：POST application/x-www-form-urlencoded 到 /snnuportal/login，
/// 字段 account / password / yys；认证结果按出口 IP 判定，页面为 GB2312 编码。
/// </summary>
public sealed class PortalClient : IDisposable
{
    private const string Root = "http://202.117.144.205:8603/snnuportal/";
    private const string PortalHost = "202.117.144.205";

    private static readonly Encoding Gb = CreateGbEncoding();

    private readonly HttpClient _http;

    private static Encoding CreateGbEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding("gb18030");
        }
        catch
        {
            return Encoding.UTF8;
        }
    }

    public PortalClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 8,
            UseCookies = true,
            CookieContainer = new CookieContainer(),
            ConnectTimeout = TimeSpan.FromSeconds(6),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        };

        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
        _http.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,*/*");
    }

    /// <summary>检查 Portal 是否可达（即是否处在校园网环境）。</summary>
    public async Task<bool> IsPortalReachableAsync(CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, Root + "login");
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>查询当前出口是否已认证在线。</summary>
    public async Task<LoginResult> GetStatusAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync(Root + "userstatus.jsp", ct).ConfigureAwait(false);
            var html = await ReadHtmlAsync(resp, ct).ConfigureAwait(false);

            if (html.Contains("当前登录账号", StringComparison.Ordinal))
            {
                var acc = Match(html, @"当前登录账号[：:]\s*(?:<[^>]+>\s*)*([^<\s]+)");
                var ip = Match(html, @"当前IP[：:]\s*(?:<[^>]+>\s*)*([0-9]{1,3}(?:\.[0-9]{1,3}){3})");
                var time = Match(html, @"登录时间[：:]\s*(?:<[^>]+>\s*)*([0-9]{4}-[0-9]{2}-[0-9]{2}\s+[0-9]{2}:[0-9]{2}:[0-9]{2})");
                return LoginResult.Online(acc, ip, time);
            }

            return LoginResult.Fail("未认证");
        }
        catch (OperationCanceledException)
        {
            return LoginResult.Fail("连接超时");
        }
        catch (Exception ex)
        {
            return LoginResult.Fail(Describe(ex));
        }
    }

    /// <summary>执行登录。会先抓取 Portal 重定向携带的 NAS 参数（wlanuserip 等）并透传。</summary>
    public async Task<LoginResult> LoginAsync(string account, string password, Operator op, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(account)) return LoginResult.Fail("账号不能为空");
        if (string.IsNullOrEmpty(password)) return LoginResult.Fail("密码不能为空");

        account = account.Trim();

        // 账号自带 @mobile / @unicom / @telecom 后缀时，用后缀覆盖出口设置
        var yys = op switch
        {
            Operator.Mobile => "mobile",
            Operator.Unicom => "unicom",
            Operator.Telecom => "telecom",
            _ => ""
        };

        var at = account.IndexOf('@');        if (at > 0)
        {
            var suffix = account[(at + 1)..].Trim().ToLowerInvariant();
            if (suffix is "mobile" or "unicom" or "telecom" or "campus" or "edu")
            {
                account = account[..at];
                yys = suffix is "campus" or "edu" ? "" : suffix;
            }
        }

        // 先探测：已在线则直接返回
        var status = await GetStatusAsync(ct).ConfigureAwait(false);
        if (status.Success) return status;

        // 抓登录页，取 NAS 参数
        var nasParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string loginPage = "";
        try
        {
            using var probe = await _http.GetAsync(Root + "login", ct).ConfigureAwait(false);
            loginPage = await ReadHtmlAsync(probe, ct).ConfigureAwait(false);

            var finalUri = probe.RequestMessage?.RequestUri;
            if (finalUri != null)
            {
                foreach (var kv in ParseQuery(finalUri.Query))
                    nasParams[kv.Key] = kv.Value;
            }
        }
        catch (OperationCanceledException)
        {
            return LoginResult.Fail("连接超时");
        }
        catch (Exception ex)
        {
            return LoginResult.Fail(Describe(ex));
        }

        if (loginPage.Contains("当前登录账号", StringComparison.Ordinal))
            return LoginResult.Online(null, null, null);

        // 有验证码则无法纯自动登录
        if (Regex.IsMatch(loginPage, @"name\s*=\s*[""']?checkcode", RegexOptions.IgnoreCase))
            return LoginResult.Fail("Portal 要求输入图形验证码，请先用浏览器登录一次");

        var form = new Dictionary<string, string>
        {
            ["account"] = account,
            ["password"] = password
        };

        if (yys.Length > 0) form["yys"] = yys;

        // 透传 NAS 侧参数（不同接入点参数名不同，多余参数不影响服务端解析）
        foreach (var key in new[] { "wlanuserip", "wlanacname", "nasip", "userip", "usermac", "mac", "subnet" })
        {
            if (nasParams.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v))
                form[key] = v;
        }

        var endpoints = new[] { Root + "login", Root + "login.jsp" };
        string lastError = "登录失败";

        foreach (var url in endpoints)
        {
            try
            {
                using var content = new StringContent(BuildFormBody(form), Gb, "application/x-www-form-urlencoded");
                using var resp = await _http.PostAsync(url, content, ct).ConfigureAwait(false);
                var html = await ReadHtmlAsync(resp, ct).ConfigureAwait(false);

                if (html.Contains("当前登录账号", StringComparison.Ordinal))
                {
                    var acc = Match(html, @"当前登录账号[：:]\s*(?:<[^>]+>\s*)*([^<\s]+)") ?? account;
                    var ip = Match(html, @"当前IP[：:]\s*(?:<[^>]+>\s*)*([0-9]{1,3}(?:\.[0-9]{1,3}){3})");
                    var time = Match(html, @"登录时间[：:]\s*(?:<[^>]+>\s*)*([0-9]{4}-[0-9]{2}-[0-9]{2}\s+[0-9]{2}:[0-9]{2}:[0-9]{2})");
                    return LoginResult.Ok("登录成功", acc, ip, time);
                }

                lastError = ExtractError(html) ?? lastError;

                // 表单仍在 => 该端点不是登录处理入口，试下一个
                if (!Regex.IsMatch(html, @"name\s*=\s*[""']?password", RegexOptions.IgnoreCase))
                    break;
            }
            catch (OperationCanceledException)
            {
                return LoginResult.Fail("连接超时");
            }
            catch (Exception ex)
            {
                lastError = Describe(ex);
            }
        }

        // 最后确认一次真实状态（有的实现登录成功但页面跳转延迟）
        var verify = await GetStatusAsync(ct).ConfigureAwait(false);
        if (verify.Success) return verify;

        return LoginResult.Fail(lastError);
    }

    private static string BuildFormBody(Dictionary<string, string> form)
    {
        var sb = new StringBuilder();
        foreach (var kv in form)
        {
            if (kv.Value is null) continue;
            if (sb.Length > 0) sb.Append('&');
            sb.Append(Uri.EscapeDataString(kv.Key)).Append('=').Append(Uri.EscapeDataString(kv.Value));
        }
        return sb.ToString();
    }

    private static async Task<string> ReadHtmlAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        if (bytes.Length == 0) return string.Empty;

        var text = Gb.GetString(bytes);
        // 部分页面实际为 UTF-8 但声明 GB2312，出现大量替换符时回退
        if (text.Contains('\uFFFD'))
        {
            var utf8 = Encoding.UTF8.GetString(bytes);
            if (!utf8.Contains('\uFFFD')) return utf8;
        }
        return text;
    }

    private static IEnumerable<KeyValuePair<string, string>> ParseQuery(string query)
    {
        var list = new List<KeyValuePair<string, string>>();
        if (string.IsNullOrEmpty(query)) return list;
        var q = query.TrimStart('?');
        foreach (var part in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = part.IndexOf('=');
            if (idx <= 0) continue;
            var k = Uri.UnescapeDataString(part[..idx]);
            var v = Uri.UnescapeDataString(part[(idx + 1)..]);
            list.Add(new KeyValuePair<string, string>(k, v));
        }
        return list;
    }

    private static string? Match(string html, string pattern)
    {
        var m = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
        return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value.Trim()) : null;
    }

    /// <summary>从 Portal 返回的页面里提取人话错误信息。</summary>
    private static string? ExtractError(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;

        var m = Regex.Match(html, @"alert\s*\(\s*[""']([^""']{2,80})[""']", RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value;

        var known = new[]
        {
            "密码错误", "密码不正确", "账号不存在", "用户名不存在", "账号或密码",
            "已欠费", "余额不足", "已达到", "超过", "停机", "被锁定", "验证码错误",
            "认证失败", "登录失败", "该账号", "不在服务范围", "绑定"
        };
        var plain = Regex.Replace(html, "<script[\\s\\S]*?</script>", " ");
        plain = Regex.Replace(plain, "<[^>]+>", " ");
        plain = WebUtility.HtmlDecode(plain);
        plain = Regex.Replace(plain, "\\s+", " ").Trim();

        foreach (var k in known)
        {
            var i = plain.IndexOf(k, StringComparison.Ordinal);
            if (i < 0) continue;
            var start = Math.Max(0, i - 20);
            var len = Math.Min(plain.Length - start, 60);
            return plain.Substring(start, len).Trim();
        }
        return null;
    }

    private static string Describe(Exception ex) => ex switch
    {
        HttpRequestException hre when hre.InnerException is System.Net.Sockets.SocketException => "无法连接 Portal（检查是否已接入校园网）",
        HttpRequestException => "无法连接 Portal（检查是否已接入校园网）",
        TaskCanceledException => "连接超时",
        _ => ex.Message
    };

    public void Dispose() => _http.Dispose();
}
