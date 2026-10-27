package io.github.shixiangliu.snnulogin;

import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.ActivityManager;
import android.content.Context;
import android.os.Build;
import android.os.PowerManager;

/** Read actual platform settings; opening a settings screen never counts as granting permission. */
final class SystemSettingsState {
    static final class Entry {
        final String title, detail, button;
        final UiModel.Tone tone;
        final boolean ready, channelBlocked;
        Entry(String title, String detail, String button, UiModel.Tone tone, boolean ready, boolean channelBlocked) {
            this.title = title; this.detail = detail; this.button = button;
            this.tone = tone; this.ready = ready; this.channelBlocked = channelBlocked;
        }
    }
    final Entry notifications, battery;
    private SystemSettingsState(Entry notifications, Entry battery) { this.notifications = notifications; this.battery = battery; }
    static SystemSettingsState read(Context context) { return new SystemSettingsState(notifications(context), battery(context)); }
    static Entry notifications(Context context) {
        try {
            NotificationManager manager = context.getSystemService(NotificationManager.class);
            if (!manager.areNotificationsEnabled()) return new Entry("通知未开启", "开启后，可以在通知栏连接、暂停或打开应用。", "去开启通知", UiModel.Tone.WARNING, false, false);
            NotificationChannel channel = manager.getNotificationChannel(LoginService.CHANNEL);
            if (channel != null && channel.getImportance() == NotificationManager.IMPORTANCE_NONE) {
                return new Entry("连接通知已关闭", "应用通知已允许，请再开启「校园网连接」这一类通知。", "开启连接通知", UiModel.Tone.WARNING, false, true);
            }
            return new Entry("通知已开启", "连接后，可以在通知栏快捷操作，无需再调整。", "查看通知设置", UiModel.Tone.GOOD, true, false);
        } catch (RuntimeException ex) {
            return new Entry("暂无法读取通知状态", "请到系统设置中确认是否允许通知。", "查看通知设置", UiModel.Tone.NEUTRAL, false, false);
        }
    }
    private static Entry battery(Context context) {
        Boolean exempt = null, restricted = null;
        try { exempt = context.getSystemService(PowerManager.class).isIgnoringBatteryOptimizations(context.getPackageName()); }
        catch (RuntimeException ignored) { }
        if (Build.VERSION.SDK_INT >= 28) {
            try { restricted = context.getSystemService(ActivityManager.class).isBackgroundRestricted(); }
            catch (RuntimeException ignored) { }
        }
        return battery(exempt, restricted);
    }
    /** A platform allowlist and a vendor's own battery switch are different facts. */
    static Entry battery(Boolean exempt, Boolean restricted) {
        if (Boolean.TRUE.equals(restricted)) return new Entry("系统限制后台运行", "请在系统设置中允许本应用后台运行。", "打开应用设置", UiModel.Tone.WARNING, false, false);
        if (Boolean.TRUE.equals(exempt)) return new Entry("已获系统省电豁免", "已允许本应用忽略 Android 电池优化。部分手机另有厂商的电池使用设置，应用无法读取，以系统页面为准。", "查看系统设置", UiModel.Tone.GOOD, true, false);
        if (exempt == null) return new Entry("以系统设置为准", "暂时无法读取系统省电状态，请在应用设置中确认后台权限。", "打开应用设置", UiModel.Tone.NEUTRAL, false, false);
        return new Entry("系统默认管理", "未获 Android 省电豁免。若锁屏后重连受影响，可在系统设置中允许后台运行。", "查看电池设置", UiModel.Tone.NEUTRAL, false, false);
    }
}
