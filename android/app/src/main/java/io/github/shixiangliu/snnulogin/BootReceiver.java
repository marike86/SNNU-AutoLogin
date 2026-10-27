package io.github.shixiangliu.snnulogin;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;

/** Credential-protected settings become available after the first unlock; no direct-boot secrets. */
public final class BootReceiver extends BroadcastReceiver {
    @Override public void onReceive(Context context, Intent intent) {
        if (!Intent.ACTION_BOOT_COMPLETED.equals(intent.getAction())) return;
        Store store = Store.get(context);
        if (store.boot() && store.automatic() && store.account().configured) LoginService.send(context, LoginService.START);
    }
}
