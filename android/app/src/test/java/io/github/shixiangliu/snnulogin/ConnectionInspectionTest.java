package io.github.shixiangliu.snnulogin;

import java.io.InterruptedIOException;
import java.nio.charset.StandardCharsets;
import java.util.Collections;
import java.util.concurrent.atomic.AtomicInteger;
import org.junit.Test;
import static org.junit.Assert.*;
import static io.github.shixiangliu.snnulogin.PortalProtocol.*;

public class ConnectionInspectionTest {
    @Test public void existingOnlineSessionIsObservedWithoutStartingAuthentication() throws Exception {
        AtomicInteger requests = new AtomicInteger();
        PortalClient.Transport transport = (method, url, headers, body, timeout) -> {
            requests.incrementAndGet(); assertEquals("GET", method); assertNull(body);
            assertTrue(url.endsWith("userstatus.jsp"));
            return new PortalClient.Response(200, Collections.emptyMap(), "当前登录账号：20241234@mobile".getBytes(StandardCharsets.UTF_8));
        };
        PortalClient.Status result = ConnectionInspection.read(transport, () -> false, new Endpoint(HOST, 8601), "");
        assertEquals(Kind.ONLINE, result.kind); assertEquals("20241234@mobile", result.session.account); assertEquals(1, requests.get());
    }
    @Test public void offlineStatusNeverSubmitsAStoredAccountOrAttemptsLogout() throws Exception {
        AtomicInteger requests = new AtomicInteger();
        PortalClient.Transport transport = (method, url, headers, body, timeout) -> {
            requests.incrementAndGet(); assertEquals("GET", method); assertNull(body);
            assertFalse(url.endsWith("logout"));
            return new PortalClient.Response(200, Collections.emptyMap(), "<input name='account'><input name='password'>".getBytes(StandardCharsets.UTF_8));
        };
        assertEquals(Kind.OFFLINE, ConnectionInspection.read(transport, () -> false, new Endpoint(HOST, 8601), "").kind);
        assertEquals(1, requests.get());
    }
    @Test public void cancellingAnOldScreenPreventsEvenTheFirstRequest() {
        AtomicInteger requests = new AtomicInteger();
        PortalClient.Transport transport = (method, url, headers, body, timeout) -> { requests.incrementAndGet(); throw new AssertionError("Cancelled screen performed I/O"); };
        assertThrows(InterruptedIOException.class, () -> ConnectionInspection.read(transport, () -> true, new Endpoint(HOST, 8601), ""));
        assertEquals(0, requests.get());
    }
}
