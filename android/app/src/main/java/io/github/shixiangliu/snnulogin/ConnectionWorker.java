package io.github.shixiangliu.snnulogin;

import java.util.concurrent.ScheduledThreadPoolExecutor;
import java.util.concurrent.TimeUnit;

/** Visible status reads and the foreground service share one serial, idle-expiring worker. */
final class ConnectionWorker {
    private static final ScheduledThreadPoolExecutor EXECUTOR = create();
    private static ScheduledThreadPoolExecutor create() {
        ScheduledThreadPoolExecutor executor = new ScheduledThreadPoolExecutor(1, job -> {
            Thread thread = new Thread(job, "campus-connection"); thread.setDaemon(true); return thread;
        });
        executor.setRemoveOnCancelPolicy(true); executor.setKeepAliveTime(3, TimeUnit.SECONDS);
        executor.allowCoreThreadTimeOut(true); executor.setExecuteExistingDelayedTasksAfterShutdownPolicy(false);
        return executor;
    }
    static ScheduledThreadPoolExecutor get() { return EXECUTOR; }
}
