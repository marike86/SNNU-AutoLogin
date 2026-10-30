using System.Text;
using SNNUAutoLogin.Services;

// 在线设备列表的解析回归测试。
//
// 两部分：
//   1. 合成样本 —— 覆盖所有分支，断言固定、可靠；
//   2. 真实样本 —— fixture_devices.html 是从校园网门户在线控制页抓下来的页面（UTF-8），
//      用来确认「真实页面能被解析」，不绑定具体设备数量（在线设备本来就时多时少）。
//
// 换学校时：替换 fixture_devices.html，并按新学校的表格结构改合成样本。

Console.OutputEncoding = Encoding.UTF8;

int failed = 0;

void Check(string name, bool ok, string detail = "")
{
    if (ok)
    {
        Console.WriteLine($"  PASS  {name}");
    }
    else
    {
        failed++;
        Console.WriteLine($"  FAIL  {name}{(detail.Length > 0 ? " — " + detail : "")}");
    }
}

// ────────────────────────── 1. 合成样本 ──────────────────────────

// 结构照搬真实门户：表头用 <th>，数据行用 <td>，
// 最后一列是操作位 —— 别的会话有「强制下线」按钮（onclick 里带会话标识），
// 本机当前这条连接没有。
const string Synthetic = """
<html><body>
<table id="ec_table">
  <tbody id="ec_table_body">
  <tr bgcolor="#e8e8e8" class="zhenwen">
    <th>上网IP</th><th>MAC地址</th><th>上网时间</th><th>上网设备</th><th>上网位置</th><th>&nbsp;</th>
  </tr>
  <tr>
    <td class="zhengwen1">10.139.31.240</td>
    <td class="zhengwen1">06:3b:96:c0:c4:4a</td>
    <td class="zhengwen1">2026-10-30 17:36:39</td>
    <td class="zhengwen1">Android手机</td>
    <td class="zhengwen1">雁塔校区无线用户</td>
    <td class="zhengwen1">
        <input type="button" value="强制下线" onclick="dm('YT-ME6001201050000000ba052aAAANNG')"/>
    </td>
  </tr>
  <tr>
    <td class="zhengwen1">10.148.3.65</td>
    <td class="zhengwen1">70:a6:cc:cc:73:65</td>
    <td class="zhengwen1">2026-10-30 09:58:58</td>
    <td class="zhengwen1">&nbsp;</td>
    <td class="zhengwen1">办公区某楼</td>
    <td class="zhengwen1">&nbsp;&nbsp;</td>
  </tr>
  </tbody>
</table>
</body></html>
""";

Console.WriteLine("在线设备列表解析测试");

var devices = DeviceListParser.Parse(Synthetic, "10.148.3.65");

Check("解析出 2 台设备", devices.Count == 2, $"实际 {devices.Count}");

if (devices.Count == 2)
{
    var phone = devices[0];
    var self = devices[1];

    Check("设备名正确", phone.DisplayName == "Android手机", phone.DisplayName);
    Check("IP 正确", phone.Ip == "10.139.31.240", phone.Ip);
    Check("MAC 正确", phone.Mac == "06:3b:96:c0:c4:4a", phone.Mac);
    Check("上线时间正确", phone.LoginTime == "2026-10-30 17:36:39", phone.LoginTime);
    Check("位置正确", phone.Location == "雁塔校区无线用户", phone.Location);

    Check("别的会话可以强制下线", phone.CanForceOffline);
    Check("别的会话不是本机", !phone.IsCurrent);
    Check("会话标识提取正确",
        phone.SessionId == "YT-ME6001201050000000ba052aAAANNG", phone.SessionId ?? "null");

    Check("本机 IP 正确", self.Ip == "10.148.3.65", self.Ip);
    Check("本机被标记为当前设备", self.IsCurrent);
    Check("本机不能强制下线（只能断开）", !self.CanForceOffline);
    Check("本机设备名为空时回退到 MAC", self.DisplayName == "70:a6:cc:cc:73:65", self.DisplayName);
    Check("本机位置仍然解析出来", self.Location == "办公区某楼", self.Location);
}

// ── 强制下线地址：保留 vloginid / ts / id，并带上 type=dm ─────────────
var frameUrl = "http://202.117.144.205:80/zili/auth_my_state.php?vloginid=20263436&ts=1793354084096&id=5212a86f8b29ba90b2c994f2016acbab";
var dmUrl = DeviceListParser.BuildForceOfflineUrl(frameUrl, "YT-ME6001201050000000ba052aAAANNG");

Check("下线地址带 type=dm", dmUrl.Contains("type=dm"), dmUrl);
Check("下线地址带 sessionid", dmUrl.Contains("sessionid=YT-ME6001201050000000ba052aAAANNG"), dmUrl);
Check("下线地址保留 vloginid", dmUrl.Contains("vloginid=20263436"), dmUrl);
Check("下线地址保留 ts", dmUrl.Contains("ts=1793354084096"), dmUrl);
Check("下线地址保留 id", dmUrl.Contains("id=5212a86f8b29ba90b2c994f2016acbab"), dmUrl);
Check("下线地址只有一个问号", dmUrl.Count(c => c == '?') == 1, dmUrl);
Check("下线地址路径不变",
    dmUrl.StartsWith("http://202.117.144.205:80/zili/auth_my_state.php"), dmUrl);

// ── 边界：空页面 / 没有表格 / 没有设备行 ────────────────────────────────
Check("空字符串不报错", DeviceListParser.Parse("", null).Count == 0);
Check("null 不报错", DeviceListParser.Parse(null!, null).Count == 0);
Check("无关页面解析出 0 台", DeviceListParser.Parse("<html><body>hi</body></html>", null).Count == 0);
Check("表头行不算设备",
    DeviceListParser.Parse("<tbody id=\"ec_table_body\"><tr><th>上网IP</th></tr></tbody>", null).Count == 0);
Check("单元格不足 5 列的行被跳过",
    DeviceListParser.Parse("<tbody id=\"ec_table_body\"><tr><td>1.2.3.4</td></tr></tbody>", null).Count == 0);
Check("没有 tbody id 时解析出 0 台（不误抓）",
    DeviceListParser.Parse("<table><tr><td>1.2.3.4</td></tr></table>", null).Count == 0);

// ── 边界：当前 IP 不在列表里时，没有设备被标为本机 ──────────────────────
var none = DeviceListParser.Parse(Synthetic, "10.255.255.255");
Check("当前 IP 不匹配时无本机设备", none.TrueForAll(d => !d.IsCurrent));
Check("当前 IP 为 null 时无本机设备",
    DeviceListParser.Parse(Synthetic, null).TrueForAll(d => !d.IsCurrent));

// ────────────────────────── 2. 真实样本 ──────────────────────────

var fixturePath = Path.Combine(AppContext.BaseDirectory, "fixture_devices.html");
if (!File.Exists(fixturePath))
{
    // 兜底：从项目目录往上找
    var dir = AppContext.BaseDirectory;
    while (dir is not null && !File.Exists(Path.Combine(dir, "fixture_devices.html")))
        dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));

    if (dir is null)
    {
        Console.WriteLine("找不到 fixture_devices.html");
        return 1;
    }

    fixturePath = Path.Combine(dir, "fixture_devices.html");
}

var html = File.ReadAllText(fixturePath, Encoding.UTF8);
var real = DeviceListParser.Parse(html, "10.148.3.65");

Check("真实页面能解析出至少 1 台设备", real.Count >= 1, $"实际 {real.Count}");
Check("真实页面每台设备都有合法 IP",
    real.TrueForAll(d => System.Text.RegularExpressions.Regex.IsMatch(d.Ip, @"^[0-9]{1,3}(\.[0-9]{1,3}){3}$")),
    string.Join(", ", real.Select(d => d.Ip)));
Check("真实页面每台设备都有 MAC",
    real.TrueForAll(d => !string.IsNullOrWhiteSpace(d.Mac)),
    string.Join(", ", real.Select(d => d.Mac)));
Check("真实页面每台设备都有上线时间",
    real.TrueForAll(d => !string.IsNullOrWhiteSpace(d.LoginTime)));
Check("真实页面至多一台是本机",
    real.Count(d => d.IsCurrent) <= 1);
Check("真实页面有会话标识的设备都能强制下线",
    real.Where(d => d.SessionId is not null).All(d => d.CanForceOffline));

Console.WriteLine(failed == 0 ? "\n全部通过" : $"\n{failed} 项失败");
return failed == 0 ? 0 : 1;
