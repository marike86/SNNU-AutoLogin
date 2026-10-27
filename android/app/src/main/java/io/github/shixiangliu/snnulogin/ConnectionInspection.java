package io.github.shixiangliu.snnulogin;

import java.io.IOException;
import java.util.function.BooleanSupplier;

/** Read-only by construction: no account/password input and no authentication/repair calls. */
final class ConnectionInspection {
    static PortalClient.Status read(PortalClient.Transport transport, BooleanSupplier cancelled,
            PortalProtocol.Endpoint cached, String approvedHost) throws IOException {
        return new PortalClient(transport, cancelled, approvedHost).status(cached);
    }
}
