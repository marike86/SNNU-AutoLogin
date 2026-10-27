package io.github.shixiangliu.snnulogin;

import org.junit.Test;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;
import static org.junit.Assert.*;

public class ConnectionPolicyTest {
    @Test public void pausingStopsExtraInternetRequestsEvenAfterLongWait() {
        assertFalse(ConnectionPolicy.shouldProbeInternet(false, 9_000_000, 0));
        assertFalse(ConnectionPolicy.shouldProbeInternet(false, 9_000_000, 100));
    }
    @Test public void automaticConnectionMaintainsItsBoundedInternetChecks() {
        assertTrue(ConnectionPolicy.shouldProbeInternet(true, 100, 0));
        assertFalse(ConnectionPolicy.shouldProbeInternet(true, 120_099, 100));
        assertTrue(ConnectionPolicy.shouldProbeInternet(true, 120_100, 100));
    }
    private static final PortalProtocol.Session OWN = new PortalProtocol.Session("20241234@mobile", "", "");
    @Test public void pauseAndCircuitBreakerPreventAutomaticLogin() {
        assertFalse(ConnectionPolicy.mayLogin(false, 0, 100));
        assertFalse(ConnectionPolicy.mayLogin(true, 200, 100));
        assertTrue(ConnectionPolicy.mayLogin(true, 200, 200));
    }
    @Test public void repairRequiresOwnConfirmedSessionFailureStreakAndCooldown() {
        assertTrue(ConnectionPolicy.mayRepair(true, 0, 2_000_000, 0, 2, true, OWN, "20241234", "mobile"));
        assertFalse(ConnectionPolicy.mayRepair(false, 0, 2_000_000, 0, 2, true, OWN, "20241234", "mobile"));
        assertFalse(ConnectionPolicy.mayRepair(true, 0, 2_000_000, 1_900_000, 2, true, OWN, "20241234", "mobile"));
        assertFalse(ConnectionPolicy.mayRepair(true, 0, 2_000_000, 0, 1, true, OWN, "20241234", "mobile"));
        assertFalse(ConnectionPolicy.mayRepair(true, 0, 2_000_000, 0, 2, false, OWN, "20241234", "mobile"));
        assertFalse(ConnectionPolicy.mayRepair(true, 0, 2_000_000, 0, 2, true, OWN, "20249999", "mobile"));
        assertFalse(ConnectionPolicy.mayRepair(true, 0, 2_000_000, 0, 2, true, OWN, "20241234", "unicom"));
    }
    @Test public void oldResultsCannotOverrideUserPauseOrNetworkSwitch() {
        RunGate gate = new RunGate();
        long old = gate.current(); gate.invalidate();
        AtomicBoolean updated = new AtomicBoolean();
        assertFalse(gate.runIfCurrent(old, () -> updated.set(true)));
        assertFalse(updated.get()); assertTrue(gate.runIfCurrent(gate.current(), () -> updated.set(true)));
    }
    @Test public void queuedLoginIsCancelledBeforeItsTurn() throws Exception {
        RunGate gate = new RunGate(); long token = gate.current();
        CountDownLatch ready = new CountDownLatch(1), release = new CountDownLatch(1);
        AtomicBoolean submitted = new AtomicBoolean();
        Thread worker = new Thread(() -> {
            ready.countDown();
            try { if (release.await(2, TimeUnit.SECONDS)) gate.runIfCurrent(token, () -> submitted.set(true)); }
            catch (InterruptedException ex) { Thread.currentThread().interrupt(); }
        });
        worker.start(); assertTrue(ready.await(2, TimeUnit.SECONDS));
        gate.invalidate(); release.countDown(); worker.join(3000);
        assertFalse(worker.isAlive()); assertFalse(submitted.get());
    }
}
