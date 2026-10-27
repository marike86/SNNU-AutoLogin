package io.github.shixiangliu.snnulogin;

import java.io.IOException;
import java.io.InterruptedIOException;
import java.security.GeneralSecurityException;
import java.util.function.BooleanSupplier;
import java.util.function.Consumer;
import static io.github.shixiangliu.snnulogin.PortalProtocol.*;

/** Confirm an existing session before replacing it; persist new credentials only after success. */
public final class AccountUpdate {
    private AccountUpdate() {}
    public enum Field { ACCOUNT, PASSWORD, OPERATOR }
    public static final class Validation {
        public final Field field;
        public final String message;
        Validation(Field field, String message) { this.field = field; this.message = message; }
    }
    public static Validation validate(String account, String password, String operator, String savedAccount, boolean configured) {
        if (!account.matches("[A-Za-z0-9._-]{4,128}")) return new Validation(Field.ACCOUNT, "请输入校园网账号，不含 @运营商后缀");
        if (!operator.isEmpty() && !operator.equals("mobile") && !operator.equals("unicom")) return new Validation(Field.OPERATOR, "请选择校园网、移动或联通");
        if (password.isEmpty() && !savedAccount.equals(account)) return new Validation(Field.PASSWORD, "更换账号时，请重新输入密码");
        if (password.isEmpty() && !configured) return new Validation(Field.PASSWORD, "请输入校园网密码");
        return null;
    }
    public interface Commit { void save() throws IOException, GeneralSecurityException; }
    public static PortalClient.Status apply(PortalClient client, PortalClient.Status current,
            PortalClient.Status consent, String account, String password, String operator,
            BooleanSupplier cancelled, Commit commit) throws IOException, GeneralSecurityException {
        return apply(client, current, consent, account, password, operator, cancelled, commit, status -> { });
    }
    public static PortalClient.Status apply(PortalClient client, PortalClient.Status current,
            PortalClient.Status consent, String account, String password, String operator,
            BooleanSupplier cancelled, Commit commit, Consumer<PortalClient.Status> progress) throws IOException, GeneralSecurityException {
        check(cancelled);
        if (current.kind == Kind.UNREACHABLE && consent != null) {
            throw new IOException("网络已变化，原账号设置已保留，请连接学校网络后重新保存");
        }
        if (current.kind == Kind.ONLINE) {
            if (!sameSession(current, consent)) throw new IOException("当前在线会话已变化，请再次保存并确认重新连接");
            String[] owner = current.session.account.split("@", 2);
            progress.accept(client.logout(current.endpoint, owner[0], owner.length == 2 ? owner[1] : ""));
            check(cancelled);
        }
        PortalClient.Status result = current;
        if (current.kind != Kind.UNREACHABLE) {
            result = client.login(current.endpoint, account, password, operator);
            if (!client.authenticatedHere || result.session == null || !result.session.matches(account, operator)) {
                throw new IOException("当前在线会话已变化，请再次保存并确认重新连接");
            }
        }
        check(cancelled);
        progress.accept(result);
        // The caller serializes this commit against pause, disconnect and network changes.
        commit.save();
        return result;
    }
    static boolean sameSession(PortalClient.Status current, PortalClient.Status consent) {
        return consent != null && consent.kind == Kind.ONLINE && consent.session != null
            && current.endpoint.equals(consent.endpoint)
            && current.session.account.equals(consent.session.account)
            && current.session.ip.equals(consent.session.ip)
            && current.session.loginTime.equals(consent.session.loginTime);
    }
    private static void check(BooleanSupplier cancelled) throws InterruptedIOException {
        if (cancelled.getAsBoolean()) throw new InterruptedIOException("操作已取消，原账号设置已保留");
    }
}
