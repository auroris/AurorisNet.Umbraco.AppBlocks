import { UMB_MODAL_CONTEXT } from "@umbraco-cms/backoffice/modal";

// Umbraco opens each nested block in its own workspace. A modal's context is attached
// to its opener, where the enclosing block or document still has its unsaved values.
export async function enclosingWorkspace(host, token, matches = () => true) {
    const visited = new Set();
    while (host && !visited.has(host)) {
        visited.add(host);
        const workspace = await host.getContext(token).catch(() => undefined);
        if (workspace && matches(workspace)) return workspace;
        host = await host.getContext(UMB_MODAL_CONTEXT).catch(() => undefined);
    }
}
