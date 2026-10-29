using System;
using System.IO;
using System.Text;

namespace SNNUAutoLogin.Services;

/// <summary>
/// 登录页解析自检：拿几份典型 Portal 页面跑一遍解析，确认表单地址、隐藏域、
/// 验证码字段与图片地址都认得出来。真机抓不到登录页时（已认证状态会被重定向）也能验证。
/// </summary>
internal static class LoginSelfTest
{
    public static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== 登录页解析自检 ===");
        sb.AppendLine($"时间 : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        var baseUri = new Uri("http://202.117.144.205:8603/snnuportal/login");

        Check(sb, "典型 JSP Portal（含 checkcode 图片）", baseUri, """
            <html><body>
            <form name="login" action="/snnuportal/login" method="post">
              <input type="hidden" name="wlanuserip" value="10.138.47.56">
              <input type="hidden" name="nasip" value="202.117.144.205">
              <input type="text" name="account" value="">
              <input type="password" name="password" value="">
              <input type="text" name="checkcode" maxlength="4">
              <img src="checkcode.jsp?r=0.123" onclick="this.src='checkcode.jsp?r='+Math.random()">
              <input type="submit" value="登录">
            </form>
            </body></html>
            """);

        Check(sb, "相对 action + 单引号属性 + randcode", baseUri, """
            <form id='f' action='login.jsp?wlanuserip=10.1.2.3'>
              <input type=hidden name=nasname value='snnu-nas-01'>
              <input name=user type=text>
              <input name=pwd type=password>
              <input name=randcode>
              <img id='cap' src='/snnuportal/image/randcode?t=998'>
            </form>
            """);

        Check(sb, "无验证码（不应误判）", baseUri, """
            <form action="http://202.117.144.205:8603/snnuportal/login" method="post">
              <input type="hidden" name="wlanacname" value="AC-01">
              <input type="text" name="account">
              <input type="password" name="password">
              <input type="submit">
            </form>
            """);

        var path = Path.Combine(Path.GetTempPath(), "snnu_login_selftest.txt");
        try { File.WriteAllText(path, sb.ToString(), Encoding.UTF8); } catch { }
        return path;
    }

    private static void Check(StringBuilder sb, string name, Uri baseUri, string html)
    {
        var page = PortalClient.ParseLoginPage(html, baseUri);

        sb.AppendLine($"[{name}]");
        sb.AppendLine($"  formAction   = {page.FormAction ?? "(未识别)"}");
        sb.AppendLine($"  hasPassword  = {page.HasPasswordField}");
        sb.AppendLine($"  captchaField = {page.CaptchaField ?? "(无)"}");
        sb.AppendLine($"  captchaImage = {page.CaptchaImageUrl ?? "(无)"}");
        sb.AppendLine($"  hidden       = {(page.Hidden.Count == 0 ? "(无)" : string.Join(", ", page.Hidden))}");
        sb.AppendLine();
    }
}
