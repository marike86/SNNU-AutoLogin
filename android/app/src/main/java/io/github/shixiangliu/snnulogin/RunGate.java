package io.github.shixiangliu.snnulogin;

/** Generation tokens prevent old network results or queued actions from winning after user input. */
public final class RunGate {
    private long generation;
    public synchronized long current() { return generation; }
    public synchronized long invalidate() { return ++generation; }
    public synchronized boolean valid(long token) { return generation == token; }
    public synchronized boolean runIfCurrent(long token, Runnable action) {
        if (generation != token) return false;
        action.run();
        return true;
    }
}
