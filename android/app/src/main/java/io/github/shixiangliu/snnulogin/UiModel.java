package io.github.shixiangliu.snnulogin;

import static io.github.shixiangliu.snnulogin.PortalProtocol.*;

/** User-facing state and next action, shared by the home screen and notification. No Android APIs. */
public final class UiModel {
    private UiModel() {}
    public enum Action { SETUP, CONNECT, DISCONNECT, WIFI, ACCOUNT, PORTAL, DETAILS, HELP, CHECK, CANCEL }
    public enum Tone { NEUTRAL, GOOD, WARNING, ERROR }
    public static final class AccountInfo {
        public final String caption, account, operator;
        AccountInfo(String caption, String account, String operator) { this.caption = caption; this.account = account; this.operator = operator; }
    }
    /** The live portal session wins over saved preferences, including when another account is online. */
    public static AccountInfo accountInfo(PortalClient.Status status, boolean wifi, String savedAccount, String savedOperator) {
        boolean online = wifi && status.kind == Kind.ONLINE && status.session != null;
        String raw = online ? status.session.account : savedAccount;
        String[] parts = raw.trim().split("@", 2);
        String user = parts[0].trim();
        String op = online ? parts.length == 2 ? parts[1].trim() : "" : savedOperator;
        String label = op.equals("mobile") ? "移动" : op.equals("unicom") ? "联通" : op.isEmpty() ? "校园网" : "未知上网方式";
        return new AccountInfo(online ? "当前账号" : user.isEmpty() ? "账号" : "已保存账号", user.isEmpty() ? "尚未设置" : user, user.isEmpty() ? "" : label);
    }
    public static final class Input {
        public PortalClient.Status status = PortalClient.Status.unreachable();
        public boolean configured, automatic, running, wifi, wifiValidated, busy, limited, portalApproved, inspecting;
        public String account = "", operator = "", stage = "", message = "";
        public Failure blockedBy = Failure.OTHER;
        public long pausedUntil, now;
    }
    public static final class View {
        public final String title, detail, button;
        public final Action action;
        public final Tone tone;
        public final boolean busy;
        View(String title, String detail, String button, Action action, Tone tone, boolean busy) {
            this.title = title; this.detail = detail; this.button = button;
            this.action = action; this.tone = tone; this.busy = busy;
        }
    }
    private static View view(String title, String detail, String button, Action action, Tone tone) {
        return new View(title, detail, button, action, tone, false);
    }
    public static boolean isProblem(String stage) {
        return stage.equals("连接需要处理") || stage.equals("账号更新未完成") || stage.equals("请重新输入密码")
            || stage.equals("自动重试暂缓") || stage.equals("发现新的认证地址") || stage.equals("后台启动受限")
            || stage.equals("设置保存失败");
    }
    public static View present(Input in) {
        Kind kind = in.wifi ? in.status.kind : Kind.UNREACHABLE;
        if (in.inspecting) return new View("正在检测连接", "正在读取当前校园网状态", "检测中", Action.CHECK, Tone.NEUTRAL, true);
        if (in.busy) {
            String title = in.stage.contains("保存") ? "正在保存并连接" : in.stage.contains("修复") ? "正在恢复连接"
                : in.stage.contains("断开") ? "正在断开校园网" : in.stage.contains("检查") ? "正在检查连接" : "正在连接校园网";
            return new View(title, "完成后会自动更新，你也可以取消本次操作", "取消本次操作", Action.CANCEL, Tone.NEUTRAL, true);
        }
        if (kind == Kind.ONLINE && in.stage.equals("账号更新未完成")) return view("已连接，设置未保存",
            "当前连接仍有效，请检查并重新保存账号设置", "检查账号设置", Action.ACCOUNT, Tone.WARNING);
        if (kind == Kind.ONLINE) {
            if (!in.configured) return view("已连接校园网", "设置账号后，掉线时也能自动连接", "设置自动连接", Action.SETUP, Tone.GOOD);
            if (!in.status.session.matches(in.account, in.operator)) {
                boolean sameUser = in.status.session.account.split("@", 2)[0].equals(in.account);
                return view(sameUser ? "上网方式与设置不同" : "已连接其他账号", "当前连接保持不变。需要切换时，请核对账号设置",
                    sameUser ? "核对上网方式" : "查看账号设置", Action.ACCOUNT, Tone.WARNING);
            }
            if (in.limited) return view("已登录，网络暂不可用", "校园网认证仍有效，正在等待网络恢复", "重新检查网络", Action.CHECK, Tone.WARNING);
            return view("已连接校园网", !in.automatic ? "自动登录已关闭，当前连接保持不变"
                    : in.running ? "自动登录已开启，掉线后自动恢复" : "当前连接正常，自动登录后台未运行",
                "断开校园网", Action.DISCONNECT, Tone.GOOD);
        }
        if (isProblem(in.stage)) {
            if (in.stage.equals("后台启动受限")) return view("自动连接尚未启动", "请允许应用在后台运行后，再返回连接", "查看后台设置", Action.HELP, Tone.WARNING);
            if (in.stage.equals("设置保存失败")) return view("设置未能保存", "请检查手机存储空间，再重新操作", "查看处理说明", Action.HELP, Tone.ERROR);
            Failure failure = in.stage.equals("自动重试暂缓") && in.blockedBy != Failure.OTHER ? in.blockedBy : failure(in.message);
            if (failure == Failure.CREDENTIAL || in.stage.equals("请重新输入密码")) return view("请检查账号密码",
                "修改校园网账号或密码后，再重新连接", "修改账号密码", Action.ACCOUNT, Tone.ERROR);
            if (failure == Failure.CAPTCHA) return view("需要网页验证", "在学校认证页面完成验证后，返回这里检查连接",
                in.portalApproved ? "打开认证页面" : "核对认证地址", in.portalApproved ? Action.PORTAL : Action.DETAILS, Tone.WARNING);
            if (failure == Failure.ACCOUNT_STATE) return view("校园网账号需要处理", shortMessage(in.message), "查看处理说明", Action.HELP, Tone.WARNING);
            if (in.stage.equals("发现新的认证地址")) return view("需要确认学校认证地址", "认证地址发生变化，核对后才能提交账号", "核对认证地址", Action.DETAILS, Tone.WARNING);
        }
        if (!in.configured) return view("设置自动登录", "保存校园网账号，连接学校 Wi-Fi 后自动登录", "设置账号", Action.SETUP, Tone.NEUTRAL);
        if (!in.wifi) return view("等待学校 Wi-Fi", in.automatic ? "连上学校 Wi-Fi 后会自动认证" : "请先在系统设置中连接学校 Wi-Fi",
            "打开 Wi-Fi 设置", Action.WIFI, Tone.NEUTRAL);
        if (in.stage.equals("账号更新未完成")) return view("新设置尚未生效", "原账号配置已保留，请检查后重新保存", "检查账号设置", Action.ACCOUNT, Tone.WARNING);
        if (in.stage.equals("连接需要处理")) return view("暂时连接不上校园网", "请确认 Wi-Fi 和校园网可用，然后重新尝试", "重新连接", Action.CONNECT, Tone.WARNING);
        if (kind == Kind.UNREACHABLE && in.wifiValidated) return view("Wi-Fi 可以上网", "学校认证状态暂未确认，正在自动重试",
            "立即检测", Action.CHECK, Tone.NEUTRAL);
        if (kind == Kind.UNREACHABLE) return view("认证服务暂不可达", "已连接 Wi-Fi，正在自动重试", "立即检测", Action.CHECK, Tone.NEUTRAL);
        if (!in.automatic) return view("自动连接已暂停", "点击连接即可恢复自动认证与掉线重连", "连接校园网", Action.CONNECT, Tone.NEUTRAL);
        if (!in.running) return view("校园网未登录", "Wi-Fi 已连接，点击连接完成校园网认证", "点击连接", Action.CONNECT, Tone.NEUTRAL);
        if (in.pausedUntil > in.now) return view("自动重试暂缓", "请先处理账号问题，需要时可手动重试", "重新连接", Action.CONNECT, Tone.WARNING);
        return view("等待自动连接", "正在等待学校认证服务，稍后会自动尝试", "立即连接", Action.CONNECT, Tone.NEUTRAL);
    }
    private static String shortMessage(String message) {
        return message.isEmpty() ? "请在学校自助服务中检查余额、套餐和账号状态" : message.substring(0, Math.min(80, message.length()));
    }
    public static boolean showProbeProgress(boolean userRequested, boolean stableState) {
        return userRequested || !stableState;
    }
}
