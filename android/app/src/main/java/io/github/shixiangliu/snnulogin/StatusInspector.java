package io.github.shixiangliu.snnulogin;

import android.content.Context;
import android.net.ConnectivityManager;
import android.net.Network;
import android.net.NetworkCapabilities;
import android.os.Handler;
import android.os.SystemClock;
import java.io.IOException;
import java.util.Objects;
import java.util.concurrent.ScheduledFuture;
import java.util.concurrent.TimeUnit;

/** Foreground-only portal observation, independent of whether automatic login is running. */
final class StatusInspector {
    private static volatile StatusInspector active;
    private final Store store;
    private final ConnectivityManager connectivity;
    private final Handler main;
    private final Runnable redraw;
    private final RunGate gate = new RunGate();
    /** While the screen is on and the app is visible, a status read is cheap enough to run often. */
    static final long VISIBLE_INTERVAL_MS = 5_000;
    private boolean visible;
    private Network previous;
    private long nextRead;
    private ScheduledFuture<?> pending;
    private volatile WifiTransport transport;
    StatusInspector(Context context, Handler main, Runnable redraw) {
        store = Store.get(context); connectivity = context.getSystemService(ConnectivityManager.class);
        this.main = main; this.redraw = redraw;
    }
    void resume() { visible = true; active = this; nextRead = 0; refresh(false); }
    void pause() {
        visible = false; cancelWork(); if (active == this) active = null;
        if (store.snapshot.inspecting && !LoginService.running()) store.snapshot = store.snapshot.withInspection(false);
    }
    static void cancelActive() { StatusInspector reader = active; if (reader != null) reader.cancelWork(); }
    private void cancelWork() {
        gate.invalidate(); if (pending != null) pending.cancel(false); pending = null;
        WifiTransport current = transport; if (current != null) current.cancel();
    }
    private Network wifi() {
        for (Network network : connectivity.getAllNetworks()) {
            NetworkCapabilities caps = connectivity.getNetworkCapabilities(network);
            if (caps != null && caps.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) && !caps.hasTransport(NetworkCapabilities.TRANSPORT_VPN)) return network;
        }
        return null;
    }
    void refresh(boolean force) {
        if (!visible || LoginService.running()) return;
        Network selected = wifi(); boolean changed = !Objects.equals(selected, previous); boolean initial = nextRead == 0;
        if (!force && !changed && (pending != null && !pending.isDone() || SystemClock.elapsedRealtime() < nextRead)) return;
        cancelWork(); previous = selected; nextRead = SystemClock.elapsedRealtime() + VISIBLE_INTERVAL_MS;
        if (selected == null) {
            store.snapshot = new Store.Snapshot(PortalClient.Status.unreachable(), "等待校园网", "请先连接学校 Wi-Fi", "Wi-Fi", false, false, false);
            redraw.run(); return;
        }
        if (force || changed || initial) {
            PortalClient.Status status = changed ? PortalClient.Status.unreachable() : store.snapshot.status;
            store.snapshot = new Store.Snapshot(status, "正在读取当前连接", "正在读取校园网状态", "Wi-Fi", false, false, false).withInspection(true);
            redraw.run();
        }
        long token = gate.current();
        pending = ConnectionWorker.get().schedule(() -> {
            WifiTransport connection = new WifiTransport(selected, () -> !gate.valid(token)); transport = connection;
            PortalClient.Status result;
            try { result = ConnectionInspection.read(connection, () -> !gate.valid(token), store.endpoint(), store.approvedHost()); }
            catch (IOException | RuntimeException ex) { result = PortalClient.Status.unreachable(); }
            finally { if (transport == connection) transport = null; }
            PortalClient.Status observed = result;
            main.post(() -> {
                if (!visible || !gate.valid(token) || LoginService.running()) return;
                if (!selected.equals(wifi())) { nextRead = 0; return; }
                if (observed.endpoint != null) try { store.endpoint(observed.endpoint); } catch (IOException ignored) { }
                String title = observed.kind == PortalProtocol.Kind.ONLINE ? "已连接" : observed.kind == PortalProtocol.Kind.OFFLINE ? "未连接" : "等待校园网";
                Store.Snapshot old = store.snapshot;
                boolean retainProblem = observed.kind != PortalProtocol.Kind.ONLINE && UiModel.isProblem(old.title);
                store.snapshot = new Store.Snapshot(observed, retainProblem ? old.title : title, retainProblem ? old.detail : "", "Wi-Fi", false, false, false);
                nextRead = SystemClock.elapsedRealtime() + VISIBLE_INTERVAL_MS; redraw.run();
            });
        }, 0, TimeUnit.SECONDS);
    }
}
