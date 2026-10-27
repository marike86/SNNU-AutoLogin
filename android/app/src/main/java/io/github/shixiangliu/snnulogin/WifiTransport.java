package io.github.shixiangliu.snnulogin;

import android.net.Network;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.InterruptedIOException;
import java.net.HttpURLConnection;
import java.net.Proxy;
import java.net.URL;
import java.util.Map;
import java.util.function.BooleanSupplier;

/** No process-wide binding, default network fallback, proxy, implicit redirects or cookie jar. */
final class WifiTransport implements PortalClient.Transport {
    private final Network network;
    private final BooleanSupplier cancelled;
    private HttpURLConnection active;
    WifiTransport(Network network, BooleanSupplier cancelled) { this.network = network; this.cancelled = cancelled; }
    synchronized void cancel() { if (active != null) active.disconnect(); }
    @Override public PortalClient.Response request(String method, String url, Map<String, String> headers,
            byte[] body, int timeout) throws IOException {
        if (cancelled.getAsBoolean()) throw new InterruptedIOException("操作已取消");
        HttpURLConnection connection = (HttpURLConnection) network.openConnection(new URL(url), Proxy.NO_PROXY);
        synchronized (this) {
            if (cancelled.getAsBoolean()) { connection.disconnect(); throw new InterruptedIOException("操作已取消"); }
            active = connection;
        }
        try {
            connection.setInstanceFollowRedirects(false);
            connection.setConnectTimeout(timeout);
            connection.setReadTimeout(timeout);
            connection.setRequestMethod(method);
            connection.setUseCaches(false);
            for (Map.Entry<String, String> header : headers.entrySet()) connection.setRequestProperty(header.getKey(), header.getValue());
            if (body != null) {
                connection.setDoOutput(true);
                connection.setFixedLengthStreamingMode(body.length);
                if (cancelled.getAsBoolean()) throw new InterruptedIOException("操作已取消");
                try (java.io.OutputStream stream = connection.getOutputStream()) { stream.write(body); }
            }
            int code = connection.getResponseCode();
            InputStream stream = code >= 400 ? connection.getErrorStream() : connection.getInputStream();
            ByteArrayOutputStream out = new ByteArrayOutputStream();
            long deadline = android.os.SystemClock.elapsedRealtime() + timeout;
            if (stream != null) try (InputStream input = stream) {
                byte[] buffer = new byte[4096];
                int read;
                while ((read = input.read(buffer)) != -1) {
                    if (cancelled.getAsBoolean()) throw new InterruptedIOException("操作已取消");
                    if (out.size() + read > 262144) throw new IOException("门户响应过大");
                    if (android.os.SystemClock.elapsedRealtime() > deadline) throw new IOException("门户响应超时");
                    out.write(buffer, 0, read);
                }
            }
            return new PortalClient.Response(code, connection.getHeaderFields(), out.toByteArray());
        } finally {
            connection.disconnect();
            synchronized (this) { if (active == connection) active = null; }
        }
    }
}
