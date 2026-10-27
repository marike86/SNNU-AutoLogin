package io.github.shixiangliu.snnulogin;

import org.junit.Test;
import static org.junit.Assert.*;
import static io.github.shixiangliu.snnulogin.PortalProtocol.*;

public class UiModelTest {
    @Test public void homeUsesLiveAccountAndCarrierInsteadOfSavedPreferences() {
        PortalClient.Status status = new PortalClient.Status(Kind.ONLINE, new Session("20241234@mobile", "", ""), new Endpoint(HOST, 8601));
        UiModel.AccountInfo info = UiModel.accountInfo(status, true, "20249999", "unicom");
        assertEquals("当前账号", info.caption); assertEquals("20241234", info.account); assertEquals("移动", info.operator);
    }
    @Test public void homeUsesLiveCampusAndUnicomWithoutConfiguration() {
        UiModel.AccountInfo campus = UiModel.accountInfo(new PortalClient.Status(Kind.ONLINE, new Session("20241234", "", ""), null), true, "", "mobile");
        assertEquals("校园网", campus.operator);
        UiModel.AccountInfo unicom = UiModel.accountInfo(new PortalClient.Status(Kind.ONLINE, new Session("20241234@unicom", "", ""), null), true, "", "");
        assertEquals("联通", unicom.operator); assertEquals("20241234", unicom.account);
    }
    @Test public void savedAccountIsNotPresentedAsAnOnlineSession() {
        UiModel.AccountInfo info = UiModel.accountInfo(PortalClient.Status.unreachable(), true, "20241234", "unicom");
        assertEquals("已保存账号", info.caption); assertEquals("20241234", info.account); assertEquals("联通", info.operator);
        assertEquals("尚未设置", UiModel.accountInfo(PortalClient.Status.unreachable(), false, "", "").account);
    }
    @Test public void lostWifiCannotKeepShowingAStaleLiveAccount() {
        UiModel.Input in = online("20241234@mobile"); in.wifi = false;
        UiModel.AccountInfo info = UiModel.accountInfo(in.status, false, "20249999", "unicom");
        assertEquals("已保存账号", info.caption); assertEquals("20249999", info.account);
        assertEquals("等待学校 Wi-Fi", UiModel.present(in).title);
    }
    @Test public void unknownCarrierIsNotMislabelledAsCampusNetwork() {
        UiModel.AccountInfo info = UiModel.accountInfo(new PortalClient.Status(Kind.ONLINE, new Session("20241234@unknown", "", ""), null), true, "", "");
        assertEquals("未知上网方式", info.operator);
    }
    private UiModel.Input configured() {
        UiModel.Input in = new UiModel.Input(); in.configured = true; in.account = "20240001"; in.operator = "mobile";
        in.running = true; in.wifi = true; in.automatic = true;
        in.status = new PortalClient.Status(Kind.OFFLINE, null, new Endpoint(HOST, 8601)); return in;
    }
    private UiModel.Input online(String account) {
        UiModel.Input in = configured();
        in.status = new PortalClient.Status(Kind.ONLINE, new Session(account, "", ""), new Endpoint(HOST, 8601)); return in;
    }
    @Test public void firstLaunchHasOneSetupActionWithoutBackendJargon() {
        UiModel.View view = UiModel.present(new UiModel.Input()); assertEquals(UiModel.Action.SETUP, view.action);
        assertFalse(view.title.contains("后台")); assertEquals("设置账号", view.button);
    }
    @Test public void existingOnlineSessionWithoutConfigurationIsNotCalledAnotherUser() {
        UiModel.Input in = online("20240001@mobile"); in.configured = false;
        assertEquals(UiModel.Action.SETUP, UiModel.present(in).action);
        assertEquals("已连接校园网", UiModel.present(in).title);
    }
    @Test public void onlineSessionKeepsDisconnectEvenWhenAutomaticIsPaused() {
        UiModel.Input in = online("20240001@mobile");
        assertEquals(UiModel.Action.DISCONNECT, UiModel.present(in).action); in.automatic = false;
        assertEquals(UiModel.Action.DISCONNECT, UiModel.present(in).action);
        assertTrue(UiModel.present(in).detail.contains("保持不变"));
    }
    @Test public void missingWifiLeadsToWifiSettingsRatherThanRepeatedAuthentication() {
        UiModel.Input in = configured(); in.wifi = false;
        assertEquals(UiModel.Action.WIFI, UiModel.present(in).action);
        assertEquals("等待学校 Wi-Fi", UiModel.present(in).title);
    }
    @Test public void stoppedServiceDoesNotFalselyClaimTheNetworkIsDisconnected() {
        UiModel.Input in = configured(); in.running = false;
        assertEquals(UiModel.Action.CONNECT, UiModel.present(in).action);
        assertEquals("校园网未登录", UiModel.present(in).title);
    }
    @Test public void stoppedBackgroundDoesNotHideAConfirmedOnlineSession() {
        UiModel.Input in = online("20240001@mobile"); in.running = false;
        assertEquals(UiModel.Action.DISCONNECT, UiModel.present(in).action);
        assertEquals("已连接校园网", UiModel.present(in).title);
        assertTrue(UiModel.present(in).detail.contains("后台未运行"));
    }
    @Test public void portalOfflineOutranksTheSystemsInternetClaim() {
        // Android may call the Wi-Fi "validated" while the campus portal still reports no session.
        UiModel.Input in = configured(); in.running = false; in.wifiValidated = true;
        UiModel.View view = UiModel.present(in);
        assertEquals("校园网未登录", view.title); assertEquals(UiModel.Action.CONNECT, view.action);
        in.automatic = false; assertEquals("自动连接已暂停", UiModel.present(in).title);
    }
    @Test public void usableWifiWithUnreachablePortalKeepsReadOnlyCheckAction() {
        UiModel.Input in = configured(); in.wifiValidated = true; in.status = PortalClient.Status.unreachable();
        assertEquals("Wi-Fi 可以上网", UiModel.present(in).title);
        assertEquals(UiModel.Action.CHECK, UiModel.present(in).action);
        assertTrue(UiModel.present(in).detail.contains("认证状态暂未确认"));
    }
    @Test public void cellularValidationNeverCountsAsWifiConnectivity() {
        UiModel.Input in = configured(); in.wifi = false; in.wifiValidated = true;
        assertEquals("等待学校 Wi-Fi", UiModel.present(in).title);
        assertEquals(UiModel.Action.WIFI, UiModel.present(in).action);
    }
    @Test public void unreachablePortalOnWifiDoesNotAssertThatWifiIsNotCampus() {
        UiModel.Input in = configured(); in.status = PortalClient.Status.unreachable();
        assertEquals("认证服务暂不可达", UiModel.present(in).title);
        assertEquals(UiModel.Action.CHECK, UiModel.present(in).action);
    }
    @Test public void unreachablePortalKeepsRetryingWithoutUserAction() {
        UiModel.Input in = configured(); in.status = PortalClient.Status.unreachable();
        UiModel.View view = UiModel.present(in);
        assertTrue(view.detail.contains("自动重试"));
        assertEquals("立即检测", view.button);
        in.wifiValidated = true;
        assertTrue(UiModel.present(in).detail.contains("自动重试"));
    }
    @Test public void foregroundInspectionDoesNotOfferAuthenticationOrPause() {
        UiModel.Input in = configured(); in.running = false; in.inspecting = true;
        UiModel.View view = UiModel.present(in);
        assertEquals("正在检测连接", view.title); assertEquals(UiModel.Action.CHECK, view.action);
        assertTrue(view.busy);
    }
    @Test public void credentialErrorGoesToAccountInsteadOfRetry() {
        UiModel.Input in = configured(); in.stage = "连接需要处理"; in.message = "您的账号密码有误";
        assertEquals(UiModel.Action.ACCOUNT, UiModel.present(in).action);
        assertEquals("修改账号密码", UiModel.present(in).button);
    }
    @Test public void firstAccountFailureStillExplainsTheErrorInsteadOfShowingWelcome() {
        UiModel.Input in = configured(); in.configured = false; in.stage = "账号更新未完成"; in.message = "密码错误";
        assertEquals(UiModel.Action.ACCOUNT, UiModel.present(in).action);
    }
    @Test public void captchaRequiresApprovedPortalBeforeOpeningBrowser() {
        UiModel.Input in = configured(); in.stage = "连接需要处理"; in.message = "请输入验证码";
        assertEquals(UiModel.Action.DETAILS, UiModel.present(in).action); in.portalApproved = true;
        assertEquals(UiModel.Action.PORTAL, UiModel.present(in).action);
    }
    @Test public void balanceProblemDoesNotRecommendChangingPassword() {
        UiModel.Input in = configured(); in.stage = "连接需要处理"; in.message = "账号已欠费";
        assertEquals(UiModel.Action.HELP, UiModel.present(in).action);
    }
    @Test public void pausedCredentialReasonSurvivesGenericPollingCopyAndRestart() {
        UiModel.Input in = configured(); in.stage = "自动重试暂缓"; in.message = "请先处理账号问题，再重新连接";
        in.blockedBy = Failure.CREDENTIAL; assertEquals(UiModel.Action.ACCOUNT, UiModel.present(in).action);
        in.blockedBy = Failure.ACCOUNT_STATE; assertEquals(UiModel.Action.HELP, UiModel.present(in).action);
    }
    @Test public void otherAccountsNeverGetADisconnectPrimaryAction() {
        assertEquals(UiModel.Action.ACCOUNT, UiModel.present(online("20249999@mobile")).action);
        UiModel.View view = UiModel.present(online("20240001@unicom"));
        assertEquals(UiModel.Action.ACCOUNT, view.action); assertEquals("核对上网方式", view.button);
    }
    @Test public void busyOperationCanBeCancelled() {
        UiModel.Input in = configured(); in.busy = true; in.stage = "正在保存并连接";
        UiModel.View view = UiModel.present(in); assertTrue(view.busy); assertEquals(UiModel.Action.CANCEL, view.action);
    }
    @Test public void limitedConnectionDoesNotLoseItsAuthenticatedState() {
        UiModel.Input in = online("20240001@mobile"); in.limited = true;
        assertEquals(UiModel.Action.CHECK, UiModel.present(in).action);
        assertEquals("已登录，网络暂不可用", UiModel.present(in).title);
    }
    @Test public void successfulConnectionWithFailedSaveIsNotPresentedAsSaved() {
        UiModel.Input in = online("20240001@mobile"); in.stage = "账号更新未完成";
        assertEquals("已连接，设置未保存", UiModel.present(in).title);
        assertEquals(UiModel.Action.ACCOUNT, UiModel.present(in).action);
    }
    @Test public void stableRoutineProbeDoesNotShowProgressButExplicitCheckDoes() {
        assertFalse(UiModel.showProbeProgress(false, true));
        assertTrue(UiModel.showProbeProgress(true, true));
        assertTrue(UiModel.showProbeProgress(false, false));
    }
    @Test public void pausedOfflineConnectionHasAnExplicitResumeAction() {
        UiModel.Input in = configured(); in.automatic = false;
        assertEquals("自动连接已暂停", UiModel.present(in).title);
        assertEquals(UiModel.Action.CONNECT, UiModel.present(in).action);
    }
    @Test public void sharedFormValidationIdentifiesTheCorrectFieldAndAllowsRetainedPassword() {
        assertEquals(AccountUpdate.Field.ACCOUNT, AccountUpdate.validate("bad@mobile", "pw", "mobile", "", false).field);
        assertEquals(AccountUpdate.Field.PASSWORD, AccountUpdate.validate("20240001", "", "mobile", "", false).field);
        assertNull(AccountUpdate.validate("20240001", "", "mobile", "20240001", true));
        assertEquals(AccountUpdate.Field.PASSWORD, AccountUpdate.validate("20240002", "", "mobile", "20240001", true).field);
        assertEquals(AccountUpdate.Field.OPERATOR, AccountUpdate.validate("20240001", "pw", "unknown", "", false).field);
    }
}
