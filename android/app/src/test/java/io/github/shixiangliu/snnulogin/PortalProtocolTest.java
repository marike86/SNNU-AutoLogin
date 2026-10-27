package io.github.shixiangliu.snnulogin;

import org.junit.Test;
import java.nio.charset.Charset;
import java.nio.charset.StandardCharsets;
import static org.junit.Assert.*;
import static io.github.shixiangliu.snnulogin.PortalProtocol.*;

public class PortalProtocolTest {
    private static final String ONLINE = "<table><tr><td>当前登录账号：</td><td><span>20241234@mobile</span></td></tr>"
        + "<tr><td>登录时间：</td><td>2026-10-25 14:06:29</td></tr><tr><td>当前IP：</td><td>10.1.2.3</td></tr></table>";
    @Test public void utf8GbkAndBomAreDecoded() {
        String text = "陕西师范大学网络Portal 密码错误";
        assertEquals(text, decode(text.getBytes(StandardCharsets.UTF_8)));
        assertEquals(text, decode(text.getBytes(Charset.forName("GB18030"))));
        assertEquals(text, decode(("\ufeff" + text).getBytes(StandardCharsets.UTF_8)));
    }
    @Test public void sessionPreservesTimeColonsAndIp() {
        Session session = session(ONLINE);
        assertNotNull(session); assertEquals("20241234@mobile", session.account);
        assertEquals("10.1.2.3", session.ip); assertEquals("2026-10-25 14:06:29", session.loginTime);
    }
    @Test public void malformedSessionsAreRejected() {
        assertNull(session("当前登录账号：<script>fake()</script>"));
        assertNull(session("当前登录账号：<b>ab</b>"));
        assertNull(session("学校账号：20241234"));
    }
    @Test public void loginFormsAcceptQuotedAndUnquotedFields() {
        assertTrue(loginForm("<input NAME = 'account'><input id=\"password\">"));
        assertTrue(loginForm("<input name=account><input name=password>"));
        assertFalse(loginForm("<input name=account><input name=passwordReset>"));
    }
    @Test public void markupNoiseAndScriptAreRemoved() {
        assertEquals("登录失败 & 请重试", cleanText("<!--secret--><script>password</script><style>x</style><b>登录失败</b>&nbsp;&amp; 请重试"));
    }
    @Test public void passwordErrorBeatsCaptchaForm() {
        String message = error("您的账号密码有误。<input name='checkcode'><form><input name='account'><input name='password'></form>");
        assertEquals("您的账号密码有误", message); assertEquals(Failure.CREDENTIAL, failure(message));
    }
    @Test public void actualCaptchaHasItsOwnCategory() {
        assertEquals(Failure.CAPTCHA, failure(error("请输入验证码。<input name='checkcode'>")));
    }
    @Test public void accountStateDoesNotAskForNewPassword() {
        for (String message : new String[]{"您的账号已欠费", "账号已锁定", "账号未开通", "在线数超限", "余额不足", "已停机"}) {
            assertEquals(message, Failure.ACCOUNT_STATE, failure(message));
            assertEquals(-1, nextDelaySeconds(failure(message), 1));
        }
    }
    @Test public void emptyFormProducesUsefulError() {
        assertEquals("账号或密码错误，请检查后重试", error("<form><input name='account'><input name='password'></form>"));
        assertEquals("账号或密码错误，请检查后重试", error("<form><input name='account'><input name='password'><input name='checkcode'></form>"));
        assertEquals("账号或密码错误，请检查后重试", error("号： 密码： 校园网： 校园网 代拨： 联通 移动 电信验证码： i"));
        assertEquals("登录失败:您的账号密码有误", error("陕西师范大学网络Portal 登录失败:您的账号密码有误 账 号： 密 码： 校园网： 校园网 代 拨： 联 通 移 动 电 信 验证码： 记住密码"));
    }
    @Test public void failureBackoffMatchesWindows() {
        assertEquals(30, nextDelaySeconds(Failure.UNREACHABLE, 100));
        assertEquals(10, nextDelaySeconds(Failure.OTHER, 1));
        assertEquals(20, nextDelaySeconds(Failure.OTHER, 2));
        assertEquals(60, nextDelaySeconds(Failure.OTHER, 20));
        assertEquals(-1, nextDelaySeconds(Failure.CREDENTIAL, 1));
        assertEquals(-1, nextDelaySeconds(Failure.CAPTCHA, 1));
    }
    @Test public void discoveryRejectsUnrelatedAndAmbiguousAddresses() {
        assertNotNull(endpoint("http://202.117.144.205:8600/snnuportal/login.jsp"));
        assertNull(endpoint("http://hotel.test:8600/hotel/login"));
        assertNull(endpoint("http://user@school.test:8600/snnuportal/login.jsp"));
        assertNull(endpoint("http://school.test:12345/snnuportal/login.jsp"));
        assertNull(endpoint("https://school.test/snnuportal/login.jsp"));
        assertNull(endpoint("//school.test/snnuportal/login.jsp"));
    }
    @Test public void trustedHostUsesDomainBoundary() {
        assertTrue(trustedHost(HOST)); assertTrue(trustedHost("portal.snnu.edu.cn"));
        assertFalse(trustedHost("portal.snnu.edu.cn.evil.test")); assertFalse(trustedHost("evilsnnu.edu.cn"));
    }
    @Test public void credentialsUseTheExistingWireFields() throws Exception {
        assertEquals("sourceurl=null&account=20241234&password=p%26%3D+%E4%B8%AD&yys=mobile&issave=", form("20241234", "p&= 中", "mobile"));
    }
    @Test public void ownershipIncludesOperatorAndExactAccount() {
        Session session = session(ONLINE);
        assertTrue(session.matches("20241234", "mobile"));
        assertFalse(session.matches("20241234", "unicom")); assertFalse(session.matches("20249999", "mobile"));
        assertTrue(new Session("20241234", "", "").matches("20241234", ""));
        assertFalse(new Session("", "", "").matches("", ""));
    }
    @Test public void reportRedactsConfiguredAndOtherAccountsAndPasswords() {
        String result = redact("20241234@mobile 20249999@unicom secret-value", "20241234", "secret-value");
        assertFalse(result.contains("20241234")); assertFalse(result.contains("20249999")); assertFalse(result.contains("secret-value"));
        assertFalse(redact("abc", "abc", "").equals("abc"));
    }
    @Test public void onlineDurationRejectsInvalidAndFutureTimes() {
        java.time.LocalDateTime now = java.time.LocalDateTime.of(2026, 10, 26, 12, 0);
        assertEquals("1 小时 30 分", onlineDuration("2026-10-26 10:30:00", now));
        assertEquals("", onlineDuration("2026-10-26 12:30:00", now));
        assertEquals("", onlineDuration("2026-02-30 12:30:00", now));
        assertEquals("", onlineDuration("garbage", now));
    }
    @Test public void maskKeepsTheAccountNotTheOperatorSuffix() {
        assertEquals("423***17", mask("42316117@mobile"));
        assertEquals("423***17", mask("42316117"));
        assertEquals("***", mask("1234"));
        assertEquals("***", mask("4231@mobile"));
    }
}
