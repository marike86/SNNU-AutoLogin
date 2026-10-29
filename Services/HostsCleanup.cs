using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SNNUAutoLogin.Services;

public enum HostsCleanStatus
{
    NothingToDo,
    Cleaned,
    Denied,     // 用户在 UAC 对话框点了「否」
    Failed
}

public readonly record struct HostsCleanResult(HostsCleanStatus Status, int Removed, string Message);

/// <summary>
/// 一次性迁移清理：1.3.x 的「网络加速」会往系统 hosts 写一段固定标记块。
/// 1.4.0 起该功能已整体移除，这里负责把残留抹掉 —— 只在检测到旧标记块时才动手。
///
/// 实现要点：整段操作走**字节**而不是字符串。hosts 可能被别的工具用 GBK 存过，
/// 按 UTF-8 读进来再写回去会把别人的中文注释变成乱码。标记块本身全是 ASCII，
/// 直接在字节流里定位起点和终点、原样拼接，块外一个字节都不碰。
/// </summary>
public static class HostsCleanup
{
    public const string BeginMark = "# >>> SNNUAutoLogin Accelerator >>>";
    public const string EndMark = "# <<< SNNUAutoLogin Accelerator <<<";

    /// <summary>Win32 ERROR_CANCELLED：UAC 被取消。</summary>
    private const int ErrorCancelled = 1223;

    public static string HostsPath
    {
        get
        {
            // 自检时可以指向一份副本，不用管理员权限就能验证「定位 → 拼接」这段逻辑
            var overridden = Environment.GetEnvironmentVariable("SNNU_HOSTS_PATH");
            if (!string.IsNullOrWhiteSpace(overridden)) return overridden;

            return Path.Combine(Environment.SystemDirectory, @"drivers\etc\hosts");
        }
    }

    // ────────────────────────── 检测 ──────────────────────────

    /// <summary>hosts 里是否还留着旧版本的标记块（读不到就当没有）。</summary>
    public static bool HasLegacyBlock()
    {
        if (!ReadRaw(out var raw)) return false;
        return FindBlock(raw).Found;
    }

    /// <summary>标记块里还剩多少条记录，用于提示文案。</summary>
    public static int CountLegacyEntries()
    {
        if (!ReadRaw(out var raw)) return 0;

        var block = FindBlock(raw);
        if (!block.Found) return 0;

        int count = 0;
        foreach (var line in Encoding.ASCII.GetString(raw, block.BodyStart, block.BodyLength).Split('\n'))
        {
            var t = line.Trim();
            if (t.Length == 0 || t[0] == '#') continue;
            count++;
        }

        return count;
    }

    private static bool ReadRaw(out byte[] raw)
    {
        try
        {
            raw = File.ReadAllBytes(HostsPath);
            return true;
        }
        catch
        {
            raw = Array.Empty<byte>();
            return false;
        }
    }

    // ────────────────────────── 还原 ──────────────────────────

    /// <summary>
    /// 删掉标记块：能直接写就直接写，否则提权跑一次子进程。
    /// <paramref name="onElevating"/> 在 UAC 弹窗之前回调，便于界面切到「等待授权」。
    /// </summary>
    public static async Task<HostsCleanResult> RemoveAsync(
        Action? onElevating = null, CancellationToken ct = default)
    {
        if (!ReadRaw(out var raw))
            return new(HostsCleanStatus.Failed, 0, "读不到系统 hosts 文件");

        var block = FindBlock(raw);
        if (!block.Found)
            return new(HostsCleanStatus.NothingToDo, 0, "hosts 里没有旧版本写入的条目");

        int removed = CountLegacyEntries();
        var cleaned = RemoveBlock(raw, block);

        // 已经是管理员（或自检时指向了可写副本）：直接落盘
        if (TryWrite(cleaned)) return new(HostsCleanStatus.Cleaned, removed, $"已还原 {removed} 条解析记录");

        string tmp = Path.Combine(Path.GetTempPath(), $"snnu_hosts_{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(tmp, cleaned, ct).ConfigureAwait(false);

            string? exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
                return new(HostsCleanStatus.Failed, 0, "取不到程序路径，无法提权");

            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = $"--clean-hosts \"{tmp}\"",
                WindowStyle = ProcessWindowStyle.Hidden
            };

            onElevating?.Invoke();

            using var proc = Process.Start(psi);
            if (proc is null)
                return new(HostsCleanStatus.Failed, 0, "提权进程未能启动");

            await proc.WaitForExitAsync(ct).ConfigureAwait(false);

            return proc.ExitCode switch
            {
                0 => new(HostsCleanStatus.Cleaned, removed, $"已还原 {removed} 条解析记录"),
                1 => new(HostsCleanStatus.Failed, 0, "写入 hosts 被系统拒绝"),
                _ => new(HostsCleanStatus.Failed, 0, $"还原未完成（退出码 {proc.ExitCode}）")
            };
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return new(HostsCleanStatus.Denied, 0, "已取消授权，hosts 未改动");
        }
        catch (Win32Exception ex)
        {
            return new(HostsCleanStatus.Failed, 0, $"无法请求管理员权限：{ex.Message}");
        }
        catch (OperationCanceledException)
        {
            return new(HostsCleanStatus.Failed, 0, "操作已取消");
        }
        catch
        {
            return new(HostsCleanStatus.Failed, 0, "还原时发生异常");
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }

    /// <summary>提权子进程入口：把已经算好的 hosts 内容原样覆盖过去。成功返回 0。</summary>
    public static int RunCleanMode(string sourcePath)
    {
        try
        {
            if (!File.Exists(sourcePath)) return 2;

            var bytes = File.ReadAllBytes(sourcePath);
            if (!TryWrite(bytes)) return 1;

            FlushDnsCache();
            return 0;
        }
        catch
        {
            return 4;
        }
    }

    private static bool TryWrite(byte[] content)
    {
        string path = HostsPath;
        string tmp = path + ".snnu.tmp";

        try
        {
            File.WriteAllBytes(tmp, content);

            if (File.Exists(path))
            {
                try { File.Replace(tmp, path, null); }
                catch
                {
                    File.Copy(tmp, path, overwrite: true);
                    File.Delete(tmp);
                }
            }
            else
            {
                File.Move(tmp, path);
            }

            FlushDnsCache();
            return true;
        }
        catch
        {
            try { File.Delete(tmp); } catch { }
            return false;
        }
    }

    [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")]
    private static extern bool DnsFlushResolverCache();

    private static void FlushDnsCache()
    {
        try { DnsFlushResolverCache(); } catch { }
    }

    // ────────────────────────── 开发自检 ──────────────────────────

    /// <summary>
    /// 只算不写：把「去掉标记块之后」的内容落到 &lt;hosts&gt;.cleaned，并输出一份报告。
    /// 配合 SNNU_HOSTS_PATH 指向副本使用，不需要管理员权限。
    /// </summary>
    public static int SelfTest(string reportPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"hosts      = {HostsPath}");

        if (!ReadRaw(out var raw))
        {
            sb.AppendLine("read       = failed");
            File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
            return 1;
        }

        var block = FindBlock(raw);
        sb.AppendLine($"bytes.in   = {raw.Length}");
        sb.AppendLine($"hasBlock   = {block.Found}");
        sb.AppendLine($"entries    = {CountLegacyEntries()}");
        sb.AppendLine($"marker.at  = {(block.Found ? $"{block.Start}..{block.End}" : "-")}");

        if (block.Found)
        {
            var cleaned = RemoveBlock(raw, block);
            var outPath = HostsPath + ".cleaned";
            File.WriteAllBytes(outPath, cleaned);

            sb.AppendLine($"bytes.out  = {cleaned.Length}");
            sb.AppendLine($"removed    = {raw.Length - cleaned.Length}");
            sb.AppendLine($"written    = {outPath}");
            sb.AppendLine("--- tail of cleaned ---");
            sb.AppendLine(Encoding.UTF8.GetString(cleaned, Math.Max(0, cleaned.Length - 400),
                Math.Min(400, cleaned.Length)));
        }

        File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
        return 0;
    }

    // ────────────────────────── 字节级拼接 ──────────────────────────
    private readonly record struct BlockRange(bool Found, int Start, int End, int BodyStart, int BodyLength);

    private static BlockRange FindBlock(byte[] raw)
    {
        var begin = Encoding.ASCII.GetBytes(BeginMark);
        var end = Encoding.ASCII.GetBytes(EndMark);

        int b = IndexOf(raw, begin, 0);
        if (b < 0) return default;

        int e = IndexOf(raw, end, b + begin.Length);
        if (e < 0) return default;

        int bodyStart = b + begin.Length;
        return new BlockRange(true, b, e + end.Length, bodyStart, e - bodyStart);
    }

    /// <summary>把标记块连同它自带的首尾空行一起去掉。</summary>
    private static byte[] RemoveBlock(byte[] raw, BlockRange block)
    {
        int start = block.Start;
        int end = block.End;

        // 往左退到行首
        while (start > 0 && raw[start - 1] != (byte)'\n') start--;

        // 往前多吞一个空行，避免文件尾留下连续两个空行
        int probe = start;
        if (probe > 1 && raw[probe - 1] == (byte)'\n')
        {
            int p = probe - 1;
            if (p > 0 && raw[p - 1] == (byte)'\r') p--;
            int lineStart = p;
            while (lineStart > 0 && raw[lineStart - 1] != (byte)'\n') lineStart--;

            bool blank = true;
            for (int i = lineStart; i < p; i++)
            {
                if (raw[i] != (byte)'\r' && raw[i] != (byte)' ' && raw[i] != (byte)'\t')
                {
                    blank = false;
                    break;
                }
            }

            if (blank) start = lineStart;
        }

        // 往右吃到行尾（含换行符）
        while (end < raw.Length && raw[end] != (byte)'\n') end++;
        if (end < raw.Length) end++;

        var result = new byte[raw.Length - (end - start)];
        Buffer.BlockCopy(raw, 0, result, 0, start);
        Buffer.BlockCopy(raw, end, result, start, raw.Length - end);
        return result;
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int from)
    {
        if (needle.Length == 0 || from < 0) return -1;

        int last = haystack.Length - needle.Length;
        for (int i = from; i <= last; i++)
        {
            if (haystack[i] != needle[0]) continue;

            int j = 1;
            while (j < needle.Length && haystack[i + j] == needle[j]) j++;
            if (j == needle.Length) return i;
        }

        return -1;
    }
}
