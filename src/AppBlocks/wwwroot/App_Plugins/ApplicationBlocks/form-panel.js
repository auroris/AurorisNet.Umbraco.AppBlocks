import { mountForm } from "./forms.js";
import { attachmentsFor } from "./form-coordination.js";
import { t } from "./runtime-text.js";

export function mountFormPanel(dialog, mountChildren) {
    const original = dialog.querySelector("form");
    original.remove();
    const renderedQuery = location.search;
    let session, form, dispose, refresh;

    async function open(url, afterSave) {
        if (session) return;
        session = new AbortController();
        const { signal } = session;
        dialog.showModal();
        dialog.scrollTop = 0;
        let source = original;
        // Umbraco rendered these choices and defaults for the master's address. When the detail key changes,
        // take the form from the published page for that address, including its access checks and language.
        if (dialog.dataset.renderQuery === "true" && url.search !== renderedQuery) {
            const status = document.createElement("p");
            status.className = "app-panel-status";
            status.setAttribute("role", "status");
            status.textContent = t("loading");
            dialog.append(status);
            try {
                const answer = await fetch(url, { signal, credentials: "same-origin", cache: "no-store" });
                if (!answer.ok) throw new Error();
                const page = new DOMParser().parseFromString(await answer.text(), "text/html");
                if (signal.aborted || !dialog.open) return;
                const target = page.getElementById(dialog.id);
                if (target?.matches(".app-block-signin")) { status.textContent = target.textContent; return; }
                source = target?.querySelector("form");
                if (!source) throw new Error();
                status.remove();
            } catch {
                if (!signal.aborted) status.textContent = t("formLoadFailed");
                return;
            }
        }
        form = document.importNode(source, true);
        // The original page already loaded the scripts contributed by these blocks.
        form.querySelectorAll("script").forEach(script => script.remove());
        form.dataset.panelQuery = url.search;
        dialog.append(form);
        form.addEventListener("app-form:loaded", () => {
            if (document.activeElement === dialog.querySelector("[data-panel-close]"))
                form.querySelector("[data-field]:enabled:not([type=hidden], [readonly])")?.focus();
        }, { once: true });
        const unmountChildren = mountChildren(form);
        const unmountForm = mountForm(form, {
            changed() { refresh = afterSave; },
            saved() { refresh = afterSave; dialog.close(); },
        });
        dispose = () => { unmountForm(); unmountChildren(); };
    }

    function canClose() {
        if (form?.getAttribute("aria-busy") === "true") return false;
        if (attachmentsFor(form).some(block => block.standing() === "working")) {
            window.alert(t("filesWorking"));
            return false;
        }
        return true;
    }
    dialog.addEventListener("click", event => {
        if (!event.target.closest("[data-panel-close], [data-app-cancel]")) return;
        event.preventDefault();
        if (canClose()) dialog.close();
    });
    dialog.addEventListener("cancel", event => { if (!canClose()) event.preventDefault(); });
    dialog.addEventListener("close", event => {
        if (event.target !== dialog) return;
        session?.abort();
        session = null;
        dispose?.();
        // A denied load may have replaced the form with its sign-in notice.
        dialog.querySelector(".app-panel-header").nextElementSibling?.remove();
        if (location.hash === "#" + dialog.id) {
            const url = new URL(location.href);
            url.hash = "";
            history.replaceState(history.state, "", url);
        }
        form = null;
        dispose = null;
        refresh?.();
        refresh = null;
    });
    dialog.addEventListener("app-form:open", event => {
        event.preventDefault();
        open(event.detail.url, event.detail.afterSave);
    });
    const launcher = document.querySelector(`[data-open-panel="${dialog.id}"]`);
    // The report supplies its own Create and Open controls, including their visibility rules.
    if (document.querySelector(`[data-form-panels~="${dialog.id}"]`)) launcher?.parentElement.remove();
    else launcher?.addEventListener("click", () => open(new URL(location.href)));
    // A picked page can be reached directly, including in a new tab.
    const openLinked = () => { if (location.hash === "#" + dialog.id) open(new URL(location.href)); };
    window.addEventListener("hashchange", openLinked);
    // Finish mounting the rest of the page before initializing a linked panel's children.
    queueMicrotask(openLinked);
}
