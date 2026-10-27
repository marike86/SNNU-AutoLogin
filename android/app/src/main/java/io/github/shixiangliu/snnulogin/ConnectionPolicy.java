package io.github.shixiangliu.snnulogin;

/** Pure, testable decision rules; no Android lifecycle or persistence dependencies. */
public final class ConnectionPolicy {
    private ConnectionPolicy() {}
    public static boolean mayLogin(boolean automatic, long pausedUntil, long now) {
        return automatic && now >= pausedUntil;
    }
    public static boolean shouldProbeInternet(boolean automatic, long now, long lastCheck) {
        return automatic && (lastCheck == 0 || now - lastCheck >= 120_000L);
    }
    public static boolean mayRepair(boolean automatic, long pausedUntil, long now, long lastRepair,
            int failedProbes, boolean authenticatedHere, PortalProtocol.Session session, String account, String operator) {
        return mayLogin(automatic, pausedUntil, now) && authenticatedHere && failedProbes >= 2
            && session != null && session.matches(account, operator)
            && (lastRepair == 0 || now - lastRepair >= 1_200_000L);
    }
}
