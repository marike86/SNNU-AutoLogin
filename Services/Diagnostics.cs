using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace SNNUAutoLogin.Services;

/// <summary>命令行自检：只读地探测 Portal 可达性与当前出口认证状态，用于排障。</summary>
public static class Diagnostics
{
    public static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== SNNUAutoLogin 诊断 ===");
        sb.AppendLine($"时间      : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"系统      : {Environment.OSVersion}");
        sb.AppendLine($"运行时    : {Environment.Version}");
        sb.AppendLine($"进程架构  : {Environment.ProcessPath}");
        sb.AppendLine();

        var store = new CredentialStore();
        store.Load();
        sb.AppendLine($"[配置] 账号        : {(string.IsNullOrWhiteSpace(store.Settings.Account) ? "(未设置)" : store.Settings.Account)}");
        sb.AppendLine($"[配置] 出口        : {store.Settings.Operator}");
        sb.AppendLine($"[配置] 密码已保存  : {(string.IsNullOrEmpty(store.LoadPassword()) ? "否" : "是")}");
        sb.AppendLine($"[配置] 开机自启    : {(StartupManager.IsEnabled() ? "已注册" : "未注册")}");
        sb.AppendLine();

        using var client = new PortalClient();

        try
        {
            bool reachable = client.IsPortalReachableAsync().GetAwaiter().GetResult();
            sb.AppendLine($"[网络] Portal 可达 : {(reachable ? "是" : "否")}");
            if (!reachable)
            {
                sb.AppendLine("       结论：当前不在校园网环境，或 Portal 服务不可达。");
                return sb.ToString();
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"[网络] Portal 探测异常: {ex.GetType().Name} {ex.Message}");
            return sb.ToString();
        }

        try
        {
            var status = client.GetStatusAsync().GetAwaiter().GetResult();
            sb.AppendLine($"[状态] 是否在线    : {(status.Success ? "在线" : "离线")}");
            sb.AppendLine($"[状态] 账号        : {status.Account ?? "—"}");
            sb.AppendLine($"[状态] IP          : {status.Ip ?? "—"}");
            sb.AppendLine($"[状态] 登录时间    : {status.LoginTime ?? "—"}");
            sb.AppendLine($"[状态] 原始信息    : {status.Message}");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"[状态] 异常: {ex.GetType().Name} {ex.Message}");
        }

        sb.AppendLine();
        sb.AppendLine("诊断完成（只读，未改动任何认证状态）");
        return sb.ToString();
    }

    public static string WriteReport()
    {
        var text = Run();
        var path = Path.Combine(Path.GetTempPath(), "snnu_diag.txt");
        try { File.WriteAllText(path, text, Encoding.UTF8); } catch { }
        return path;
    }
}
