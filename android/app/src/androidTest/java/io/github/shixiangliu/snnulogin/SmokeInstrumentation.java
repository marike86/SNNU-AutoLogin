package io.github.shixiangliu.snnulogin;

import android.app.Activity;
import android.app.Instrumentation;
import android.content.Intent;
import android.graphics.Bitmap;
import android.os.Bundle;
import java.io.File;
import java.io.FileOutputStream;

/** Platform-only device checks. Run against the separate debug package, never a user's release. */
public final class SmokeInstrumentation extends Instrumentation {
    private int failures, index;
    private final int total = 9;
    @Override public void onCreate(Bundle arguments) { super.onCreate(arguments); start(); }
    private interface Check { void run() throws Exception; }
    private void check(String name, Check check) {
        Bundle result = new Bundle();
        result.putString("id", "SNNU"); result.putString("class", getClass().getName()); result.putString("test", name);
        result.putInt("current", ++index); result.putInt("numtests", total);
        sendStatus(1, result);
        try { check.run(); sendStatus(0, result); }
        catch (Throwable failure) {
            failures++; result.putString("stack", android.util.Log.getStackTraceString(failure));
            result.putString("stream", "\n" + name + ": " + failure); sendStatus(-2, result);
        }
    }
    @Override public void onStart() {
        if (!getTargetContext().getPackageName().endsWith(".debug")) {
            Bundle result = new Bundle(); result.putString("stream", "Device checks require the isolated debug package.");
            finish(Activity.RESULT_CANCELED, result); return;
        }
        Store initial = Store.get(getTargetContext());
        if (initial.account().configured || initial.automatic() || LoginService.running()) {
            Bundle result = new Bundle(); result.putString("stream", "Use a clean debug installation; existing settings were not changed.");
            finish(Activity.RESULT_CANCELED, result); return;
        }
        final MainActivity[] activity = new MainActivity[1];
        check("launchNativeActivity", () -> {
            activity[0] = (MainActivity) startActivitySync(new Intent(getTargetContext(), MainActivity.class)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
            waitForIdleSync();
            long deadline = android.os.SystemClock.elapsedRealtime() + 15_000;
            while (Store.get(getTargetContext()).snapshot.inspecting && android.os.SystemClock.elapsedRealtime() < deadline) Thread.sleep(25);
            if (Store.get(getTargetContext()).snapshot.inspecting) throw new AssertionError("Foreground observation exceeded its timeout budget");
            if (LoginService.running() || Store.get(getTargetContext()).automatic()) throw new AssertionError("Read-only launch started automatic authentication");
            runOnMainSync(() -> { callActivityOnPause(activity[0]); activity[0].onNewIntent(new Intent().putExtra("page", "home")); });
            if (activity[0] == null || activity[0].isFinishing()) throw new AssertionError("Activity did not launch");
            Bitmap bitmap = getUiAutomation().takeScreenshot();
            if (bitmap != null) {
                File image = new File(getTargetContext().getExternalFilesDir(null), "home-smoke.png");
                try (FileOutputStream stream = new FileOutputStream(image)) { bitmap.compress(Bitmap.CompressFormat.PNG, 100, stream); }
                bitmap.recycle();
            }
        });
        check("guidedHomeAndInlineValidation", () -> runOnMainSync(() -> {
            android.widget.Button action = activity[0].findViewById(R.id.home_action);
            if (action == null || !action.getText().toString().contains("设置")) throw new AssertionError("Setup must be the primary first-run action");
            if (countVisibleText(activity[0].getWindow().getDecorView(), "设置账号") > 1
                    || countVisibleText(activity[0].getWindow().getDecorView(), "检查连接") != 0) throw new AssertionError("Home has duplicate or advanced actions");
            action.performClick();
            android.widget.EditText account = activity[0].findViewById(R.id.account);
            android.widget.EditText password = activity[0].findViewById(R.id.password);
            android.widget.Button save = activity[0].findViewById(R.id.save_account);
            account.setText(""); save.performClick();
            if (account.getError() == null) throw new AssertionError("Account error must be attached to its field");
            account.setText("testonly42");
            if (account.getError() != null) throw new AssertionError("Editing must clear the obsolete account error");
            save.performClick();
            if (password.getError() == null) throw new AssertionError("Missing password must focus the password field");
            if (Store.get(getTargetContext()).account().configured) throw new AssertionError("Invalid form persisted credentials");
            activity[0].onBackPressed();
        }));
        check("revealedPasswordNeverEntersSavedState", () -> runOnMainSync(() -> {
            ((android.widget.Button) activity[0].findViewById(R.id.home_action)).performClick();
            android.widget.EditText account = activity[0].findViewById(R.id.account);
            android.widget.EditText password = activity[0].findViewById(R.id.password);
            account.setText("testonly42"); password.setText("synthetic-ui-only");
            android.view.View reveal = findText(activity[0].getWindow().getDecorView(), "显示");
            if (reveal == null) throw new AssertionError("Password visibility control missing");
            reveal.performClick();
            Bundle state = new Bundle(); activity[0].onSaveInstanceState(state);
            if (!"testonly42".equals(state.getString("account-draft"))) throw new AssertionError("Non-secret account draft was not retained");
            android.os.Parcel parcel = android.os.Parcel.obtain();
            try {
                state.writeToParcel(parcel, 0); byte[] bytes = parcel.marshall();
                if (contains(bytes, "synthetic-ui-only".getBytes(java.nio.charset.StandardCharsets.UTF_8))
                    || contains(bytes, "synthetic-ui-only".getBytes(java.nio.charset.StandardCharsets.UTF_16LE))) throw new AssertionError("Password leaked into saved state");
            } finally { parcel.recycle(); }
            activity[0].onBackPressed();
            if (activity[0].findViewById(R.id.password) != null) throw new AssertionError("Password page survived navigation");
        }));
        check("settingsFeedbackWithoutExtraConnectionOptions", () -> runOnMainSync(() -> {
            activity[0].onNewIntent(new Intent().putExtra("page", "background"));
            android.widget.TextView notice = activity[0].findViewById(R.id.notification_status);
            android.widget.TextView battery = activity[0].findViewById(R.id.battery_status);
            SystemSettingsState actual = SystemSettingsState.read(getTargetContext());
            if (notice == null || !notice.getText().toString().equals(actual.notifications.title)) throw new AssertionError("Notification state is missing");
            if (battery == null || !battery.getText().toString().equals(actual.battery.title)) throw new AssertionError("Battery state is missing");
            activity[0].onNewIntent(new Intent().putExtra("page", "settings"));
            android.view.View root = activity[0].getWindow().getDecorView();
            if (findText(root, "连接选项") != null || findText(root, "暂停时的联网检查") != null) throw new AssertionError("Redundant connection options survived");
            // Turning automatic login off is the only way to stop the service; no separate switch remains.
            if (findText(root, "后台运行") != null) throw new AssertionError("Separate background switch survived");
            activity[0].onBackPressed();
        }));
        check("helpHasDirectBackgroundAction", () -> runOnMainSync(() -> {
            activity[0].onNewIntent(new Intent().putExtra("page", "help"));
            android.view.View root = activity[0].getWindow().getDecorView();
            if (findText(root, "开始使用") == null) throw new AssertionError("Basic usage instructions missing");
            android.view.View action = activity[0].findViewById(R.id.help_background);
            if (action == null || !action.isShown()) throw new AssertionError("Background settings action should be directly available");
            action.performClick();
            if (activity[0].findViewById(R.id.notification_status) == null) throw new AssertionError("Help did not open relevant settings");
            activity[0].onBackPressed(); activity[0].onBackPressed();
        }));
        check("homeShowsAccountAndCarrierFromCorrectSource", () -> {
            Store store = Store.get(getTargetContext()); Store.Snapshot original = store.snapshot;
            try {
                store.saveAccount("20241234", "synthetic-ui-only", "mobile");
                runOnMainSync(() -> {
                    store.snapshot = new Store.Snapshot(new PortalClient.Status(PortalProtocol.Kind.ONLINE,
                        new PortalProtocol.Session("20241234@mobile", "192.0.2.10", ""), new PortalProtocol.Endpoint(PortalProtocol.HOST, 8601)),
                        "已连接", "", "Wi-Fi", false, false, false);
                    activity[0].onNewIntent(new Intent().putExtra("page", "home"));
                    if (!"20241234".contentEquals(((android.widget.TextView) activity[0].findViewById(R.id.home_account)).getText())
                        || !"移动".contentEquals(((android.widget.TextView) activity[0].findViewById(R.id.home_operator)).getText())) throw new AssertionError("Account or carrier absent from home");
                });
                waitForIdleSync();
                Thread.sleep(350); // Let the rendered frame catch up with the verified native view tree.
                Bitmap bitmap = getUiAutomation().takeScreenshot();
                if (bitmap != null) {
                    try (FileOutputStream out = new FileOutputStream(new File(getTargetContext().getExternalFilesDir(null), "home-account-smoke.png"))) { bitmap.compress(Bitmap.CompressFormat.PNG, 100, out); }
                    bitmap.recycle();
                }
                store.saveAccount("20249999", "synthetic-ui-only", "unicom");
                runOnMainSync(() -> {
                    android.net.ConnectivityManager manager = getTargetContext().getSystemService(android.net.ConnectivityManager.class);
                    boolean wifi = false;
                    for (android.net.Network network : manager.getAllNetworks()) {
                        android.net.NetworkCapabilities caps = manager.getNetworkCapabilities(network);
                        if (caps != null && caps.hasTransport(android.net.NetworkCapabilities.TRANSPORT_WIFI) && !caps.hasTransport(android.net.NetworkCapabilities.TRANSPORT_VPN)) wifi = true;
                    }
                    activity[0].onNewIntent(new Intent().putExtra("page", "home"));
                    String user = ((android.widget.TextView) activity[0].findViewById(R.id.home_account)).getText().toString();
                    String carrier = ((android.widget.TextView) activity[0].findViewById(R.id.home_operator)).getText().toString();
                    if (!user.equals(wifi ? "20241234" : "20249999") || !carrier.equals(wifi ? "移动" : "联通")) throw new AssertionError("Saved preferences overwrote the real session identity");
                });
            } finally {
                store.forget();
                runOnMainSync(() -> { store.snapshot = original; activity[0].onNewIntent(new Intent().putExtra("page", "home")); });
            }
        });
        check("keystoreRoundTrip", () -> {
            Store store = Store.get(getTargetContext());
            if (store.account().configured) throw new AssertionError("Use a clean debug installation; existing account was not changed");
            try {
                store.saveAndEnable("test-only-account", "test-only-secret&=中文", "mobile");
                if (!store.automatic()) throw new AssertionError("Credentials and automatic login were not committed together");
                if (!"test-only-secret&=中文".equals(store.password())) throw new AssertionError("Keystore round-trip failed");
                String preferences = getTargetContext().getSharedPreferences("settings", 0).getString("password", "");
                if (preferences.contains("test-only-secret")) throw new AssertionError("Plaintext password persisted");
                if (store.diagnostic().contains("test-only-secret")) throw new AssertionError("Password leaked to report");
            } finally { store.forget(); }
        });
        check("turningAutomaticOffStopsTheService", () -> {
            Store store = Store.get(getTargetContext());
            // Android 12+ blocks a foreground-service start from the background; put the app in front first.
            runOnMainSync(() -> activity[0].startActivity(new Intent(activity[0], MainActivity.class)
                .addFlags(Intent.FLAG_ACTIVITY_REORDER_TO_FRONT)));
            try { Thread.sleep(600); } catch (InterruptedException ex) { Thread.currentThread().interrupt(); }
            // Baseline: the service may still run from an earlier check.
            runOnMainSync(() -> LoginService.send(getTargetContext(), LoginService.STOP));
            if (!waitForRunning(false)) throw new AssertionError("Service kept running after stop");
            runOnMainSync(() -> LoginService.send(getTargetContext(), LoginService.RESUME));
            if (!waitForRunning(true)) throw new AssertionError("Service did not start");
            if (!waitFor(() -> store.automatic())) throw new AssertionError("Resume did not enable automatic login");
            // Turning automatic login off is the only way to stop the background; there is no separate switch.
            runOnMainSync(() -> LoginService.send(getTargetContext(), LoginService.PAUSE));
            if (!waitForRunning(false)) throw new AssertionError("Pausing did not stop the service");
            if (!waitFor(() -> !store.automatic())) throw new AssertionError("Pause unexpectedly kept automatic login on");
        });
        check("passwordDraftNeverCrossesBinder", () -> {
            final Intent[] captured = new Intent[1];
            android.content.ContextWrapper intercept = new android.content.ContextWrapper(getTargetContext()) {
                @Override public android.content.ComponentName startForegroundService(Intent intent) {
                    captured[0] = intent;
                    return new android.content.ComponentName(getTargetContext(), LoginService.class);
                }
            };
            if (!LoginService.updateAccount(intercept, "test-only-account", "synthetic-secret", "mobile", null)) {
                throw new AssertionError("Draft command was not created");
            }
            try {
                Bundle extras = captured[0].getExtras();
                if (extras == null || extras.size() != 1 || !extras.containsKey("update-id")) {
                    throw new AssertionError("Only an operation ID may cross Binder");
                }
            } finally { LoginService.send(intercept, LoginService.REFRESH); }
        });
        if (activity[0] != null) runOnMainSync(() -> activity[0].finish());
        Bundle result = new Bundle(); result.putString("stream", "\nSNNU: " + (total - failures) + "/" + total + " device checks passed\n");
        finish(failures == 0 ? Activity.RESULT_OK : Activity.RESULT_CANCELED, result);
    }
    private static boolean waitFor(java.util.function.BooleanSupplier condition) {
        long deadline = android.os.SystemClock.elapsedRealtime() + 15000;
        while (!condition.getAsBoolean() && android.os.SystemClock.elapsedRealtime() < deadline) {
            try { Thread.sleep(50); } catch (InterruptedException ex) { Thread.currentThread().interrupt(); return false; }
        }
        return condition.getAsBoolean();
    }
    private static boolean waitForRunning(boolean expected) {
        long deadline = android.os.SystemClock.elapsedRealtime() + 15000;
        while (LoginService.running() != expected && android.os.SystemClock.elapsedRealtime() < deadline) {
            try { Thread.sleep(50); } catch (InterruptedException ex) { Thread.currentThread().interrupt(); return false; }
        }
        return LoginService.running() == expected;
    }
    private static android.view.View findText(android.view.View view, String text) {
        if (view instanceof android.widget.TextView && ((android.widget.TextView) view).getText().toString().equals(text)) return view;
        if (view instanceof android.view.ViewGroup) {
            android.view.ViewGroup group = (android.view.ViewGroup) view;
            for (int i = 0; i < group.getChildCount(); i++) { android.view.View found = findText(group.getChildAt(i), text); if (found != null) return found; }
        }
        return null;
    }
    private static int countVisibleText(android.view.View view, String text) {
        if (!view.isShown()) return 0;
        int count = view instanceof android.widget.TextView && ((android.widget.TextView) view).getText().toString().equals(text) ? 1 : 0;
        if (view instanceof android.view.ViewGroup) {
            android.view.ViewGroup group = (android.view.ViewGroup) view;
            for (int i = 0; i < group.getChildCount(); i++) count += countVisibleText(group.getChildAt(i), text);
        }
        return count;
    }
    private static boolean contains(byte[] bytes, byte[] target) {
        for (int i = 0; i <= bytes.length - target.length; i++) {
            int j = 0; while (j < target.length && bytes[i + j] == target[j]) j++;
            if (j == target.length) return true;
        }
        return false;
    }
}
