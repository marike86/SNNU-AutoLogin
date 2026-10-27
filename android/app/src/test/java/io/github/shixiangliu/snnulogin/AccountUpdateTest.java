package io.github.shixiangliu.snnulogin;

import org.junit.Test;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.atomic.AtomicBoolean;
import static org.junit.Assert.*;
import static io.github.shixiangliu.snnulogin.PortalProtocol.*;

public class AccountUpdateTest {
    private static final Endpoint ENDPOINT = new Endpoint(HOST, 8601);
    private static final String FORM = "<input name='account'><input name='password'>";
    private static final String OLD = "当前登录账号：20240001@mobile";
    private static final String NEW = "当前登录账号：20240002@unicom";
    private static PortalClient.Status online(String account) {
        return new PortalClient.Status(Kind.ONLINE, new Session(account, "", ""), ENDPOINT);
    }
    private static final class Script implements PortalClient.Transport {
        final Queue<String> responses = new ArrayDeque<>();
        final List<String> urls = new ArrayList<>(), methods = new ArrayList<>();
        Script(String... responses) { this.responses.addAll(Arrays.asList(responses)); }
        @Override public PortalClient.Response request(String method, String url, Map<String, String> headers, byte[] body, int timeout) throws IOException {
            urls.add(url); methods.add(method);
            if (responses.isEmpty()) throw new IOException("No response");
            return new PortalClient.Response(200, Collections.emptyMap(), responses.remove().getBytes(StandardCharsets.UTF_8));
        }
    }
    @Test public void confirmedSwitchLogsOutAndAuthenticatesBeforeSaving() throws Exception {
        Script script = new Script(OLD, FORM, FORM, FORM, FORM, NEW);
        PortalClient client = new PortalClient(script, () -> false, "");
        AtomicBoolean saved = new AtomicBoolean();
        PortalClient.Status current = online("20240001@mobile");
        PortalClient.Status result = AccountUpdate.apply(client, current, current, "20240002", "pw", "unicom", () -> false, () -> {
            assertEquals("POST", script.methods.get(script.methods.size() - 1)); saved.set(true);
        });
        assertTrue(saved.get()); assertTrue(result.session.matches("20240002", "unicom"));
        assertEquals(1, script.urls.stream().filter(url -> url.endsWith("logoff")).count());
    }
    @Test public void failedNewPasswordKeepsOldSettings() {
        Script script = new Script(OLD, FORM, FORM, FORM, FORM, "您的账号密码有误", FORM);
        AtomicBoolean saved = new AtomicBoolean();
        PortalClient.Status current = online("20240001@mobile");
        IOException error = assertThrows(IOException.class, () -> AccountUpdate.apply(new PortalClient(script, () -> false, ""),
            current, current, "20240002", "bad-password", "unicom", () -> false, () -> saved.set(true)));
        assertFalse(saved.get()); assertEquals(Failure.CREDENTIAL, failure(error.getMessage()));
    }
    @Test public void changedSessionRequiresNewConsentWithoutSendingAnything() {
        Script script = new Script(); AtomicBoolean saved = new AtomicBoolean();
        assertThrows(IOException.class, () -> AccountUpdate.apply(new PortalClient(script, () -> false, ""),
            online("20249999@mobile"), online("20240001@mobile"), "20240002", "pw", "unicom", () -> false, () -> saved.set(true)));
        assertTrue(script.urls.isEmpty()); assertFalse(saved.get());
    }
    @Test public void newlyOnlineSessionCannotBeDisconnectedWithoutConsent() {
        Script script = new Script(); AtomicBoolean saved = new AtomicBoolean();
        assertThrows(IOException.class, () -> AccountUpdate.apply(new PortalClient(script, () -> false, ""),
            online("20240001@mobile"), null, "20240002", "pw", "unicom", () -> false, () -> saved.set(true)));
        assertTrue(script.urls.isEmpty()); assertFalse(saved.get());
    }
    @Test public void cancellationPreventsLogoutAndSettingsCommit() {
        Script script = new Script(); AtomicBoolean saved = new AtomicBoolean();
        PortalClient.Status current = online("20240001@mobile");
        assertThrows(IOException.class, () -> AccountUpdate.apply(new PortalClient(script, () -> true, ""),
            current, current, "20240002", "pw", "unicom", () -> true, () -> saved.set(true)));
        assertTrue(script.urls.isEmpty()); assertFalse(saved.get());
    }
    @Test public void cancellationAfterLoginStillPreventsSettingsCommit() {
        Script script = new Script(FORM, FORM, NEW);
        AtomicBoolean saved = new AtomicBoolean();
        PortalClient client = new PortalClient(script, () -> false, "");
        assertThrows(IOException.class, () -> AccountUpdate.apply(client, new PortalClient.Status(Kind.OFFLINE, null, ENDPOINT),
            null, "20240002", "pw", "unicom", () -> client.authenticatedHere, () -> saved.set(true)));
        assertFalse(saved.get());
    }
    @Test public void noCampusNetworkAllowsSavingForLaterWithoutHttp() throws Exception {
        AtomicBoolean saved = new AtomicBoolean();
        assertEquals(Kind.UNREACHABLE, AccountUpdate.apply(null, PortalClient.Status.unreachable(), null,
            "20240002", "pw", "unicom", () -> false, () -> saved.set(true)).kind);
        assertTrue(saved.get());
    }
    @Test public void raceToOnlineDoesNotSaveAnUnverifiedPassword() {
        Script script = new Script(NEW); AtomicBoolean saved = new AtomicBoolean();
        assertThrows(IOException.class, () -> AccountUpdate.apply(new PortalClient(script, () -> false, ""),
            new PortalClient.Status(Kind.OFFLINE, null, ENDPOINT), null, "20240002", "wrong", "unicom", () -> false, () -> saved.set(true)));
        assertFalse(saved.get()); assertFalse(script.methods.contains("POST"));
    }
    @Test public void failureAfterLogoutReportsOfflineInsteadOfStaleOnlineState() {
        Script script = new Script(OLD, FORM, FORM, FORM, FORM, "您的账号密码有误", FORM);
        PortalClient.Status current = online("20240001@mobile");
        final PortalClient.Status[] observed = {current};
        assertThrows(IOException.class, () -> AccountUpdate.apply(new PortalClient(script, () -> false, ""), current, current,
            "20240002", "bad", "unicom", () -> false, () -> { }, status -> observed[0] = status));
        assertEquals(Kind.OFFLINE, observed[0].kind);
    }
    @Test public void networkLostAfterOnlineConsentDoesNotSaveUnverifiedSettings() {
        AtomicBoolean saved = new AtomicBoolean();
        assertThrows(IOException.class, () -> AccountUpdate.apply(null, PortalClient.Status.unreachable(), online("20240001@mobile"),
            "20240002", "pw", "unicom", () -> false, () -> saved.set(true)));
        assertFalse(saved.get());
    }
}
