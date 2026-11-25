import { request, withQuery } from "./data-api.js";

import { t, textForPage } from "./runtime-text.js";

const selectionMessage = "appblocks:account-picked";

// Each instance binds only its configured fields and accepts messages only from its own popup.
export function mountAccountPicker(box, {
    popupUrl = new URL(box?.dataset.popup || "account-picker.html", import.meta.url),
    keyParameter = box?.dataset.keyParameter || "key", keyOf = person => person[keyParameter],
    blockAlias = box?.dataset.blockAlias
} = {}) {
    const form = box?.closest("[data-app-form]");
    if (!form || box.dataset.mounted) return;
    const settings = JSON.parse(box.dataset.settings), endpoint = new URL(box.dataset.endpoint, location.origin);
    if (endpoint.origin !== location.origin) return;
    const field = key => form.querySelector(`[data-field="${CSS.escape(key)}"]`);
    const boundName = settings.nameField ? field(settings.nameField) : null;
    const name = box.querySelector("[data-current-name]") || boundName, identity = field(settings.identityField);
    const mappings = Object.entries(settings.fieldMappings || {}), fallbacks = settings.fallbackFields || {};
    const selectionMappings = [...mappings, ...Object.entries(settings.prefillMappings || {})];
    if (!name || !identity || (settings.nameField && !boundName) || selectionMappings.some(([, key]) => !field(key))) return;
    box.dataset.mounted = "true";
    const find = box.querySelector("[data-find]"), manual = box.querySelector("[data-manual]"), status = box.querySelector("[data-status]");
    const normalize = (key, value) => (settings.compactUpperFields || []).includes(key) ? (value || "").replace(/\s/g, "").toUpperCase() : value || "";
    if (name === boundName) {
        const slot = blockAlias && box.closest('[data-content-element-type-alias="' + CSS.escape(blockAlias) + '"]');
        const row = document.createElement("div"); row.className = "app-person-control";
        name.before(row); row.append(name, box);
        if (slot && !slot.contains(name)) { slot.classList.add("app-picker-slot"); slot.hidden = true; }
    }
    identity.readOnly = true;
    let popup, pending, generation = 0;
    const editable = () => !identity.matches(":disabled") && !(boundName || name).matches(":disabled") && !form.classList.contains("app-people-readonly");
    const changed = input => {
        input.dispatchEvent(new Event("input", { bubbles: true }));
        input.dispatchEvent(new Event("change", { bubbles: true }));
    };
    function lock() {
        name.readOnly = !!identity.value || !settings.allowManualEntry;
        for (const [, key] of mappings) {
            const mirror = fallbacks[key];
            field(key).readOnly = !!identity.value ? !mirror || !!field(mirror)?.value : !settings.allowManualEntry;
        }
        manual.hidden = !(settings.allowsEmpty || settings.allowManualEntry) || !identity.value;
        find.disabled = manual.disabled = !editable();
    }
    function close() { ++generation; pending?.abort(); pending = null; if (popup && !popup.closed) popup.close(); popup = null; }
    async function loaded() {
        close(); lock(); status.textContent = "";
        if (boundName) { name.value = boundName.value; return; }
        const key = identity.value;
        name.value = key;
        if (!key) return;
        const current = generation;
        pending = new AbortController();
        try {
            const selected = await request(withQuery(endpoint, { [keyParameter]: key }), { signal: pending.signal });
            if (current === generation && identity.value === key && keyOf(selected.person) === key) name.value = selected.person.displayName;
        } catch { /* A saved key remains visible if its account is no longer available. */ }
    }
    const lifetime = new AbortController();
    const listen = { signal: lifetime.signal };
    form.addEventListener("app-form:loaded", loaded, listen);
    form.addEventListener("app-form:saved", loaded, listen);
    // Field rules can enable or disable either binding after another field changes.
    const refresh = () => queueMicrotask(lock);
    form.addEventListener("input", refresh, listen);
    form.addEventListener("change", refresh, listen);
    if (boundName && boundName !== name) name.addEventListener("input", () => {
        boundName.value = name.value;
        changed(boundName);
    }, listen);
    window.addEventListener("pagehide", close, { signal: lifetime.signal });
    window.addEventListener("message", async event => {
        if (event.origin !== location.origin || !popup || event.source !== popup || event.data?.type !== selectionMessage) return;
        if (!editable()) { close(); return; }
        const selectedKey = event.data.key;
        if (typeof selectedKey !== "string" || !selectedKey) return;
        const current = ++generation; pending?.abort(); pending = new AbortController();
        status.textContent = t("pickerChecking");
        try {
            // The popup's label and attributes are never used as canonical values.
            const selected = await request(withQuery(endpoint, { [keyParameter]: selectedKey }), { signal: pending.signal });
            if (current !== generation || !editable() || !popup || popup.closed) return;
            const person = selected.person;
            if (!person || keyOf(person) !== selectedKey || typeof person.displayName !== "string") throw new Error("Invalid selection");
            const attributes = Object.fromEntries(Object.entries(person.attributes || {}).map(([key, value]) => [key.toLowerCase(), value]));
            identity.value = selectedKey; name.value = person.displayName;
            if (boundName) boundName.value = name.value;
            for (const [attribute, key] of selectionMappings) {
                const value = attribute.toLowerCase() === "displayname" ? person.displayName : attributes[attribute.toLowerCase()];
                field(key).value = normalize(key, value);
                if (fallbacks[key] && field(fallbacks[key])) field(fallbacks[key]).value = field(key).value;
            }
            // Dispatch only after every field is updated, so observers see one complete identity.
            for (const input of [identity, boundName || name, ...selectionMappings.map(([, key]) => field(key))]) changed(input);
            lock(); status.classList.remove("app-picker-error");
            status.textContent = t("pickerSelected", { name: person.displayName });
            if (selected.cached) status.textContent += " " + t("pickerCached");
            const missing = selectionMappings.map(([, key]) => field(key)).find(input =>
                !input.value && !input.readOnly && !input.matches(":disabled") && input.getClientRects().length);
            close(); window.focus(); (missing || find).focus();
        } catch (error) {
            if (current !== generation || error.name === "AbortError") return;
            status.textContent = t("pickerUnavailable");
            status.classList.add("app-picker-error");
        }
    }, { signal: lifetime.signal });
    for (const [, key] of mappings) field(key).addEventListener("change", () => {
        if (!field(key).readOnly) field(key).value = normalize(key, field(key).value);
    });
    manual.addEventListener("click", () => {
        if (!editable() || !(settings.allowManualEntry || settings.allowsEmpty)) return;
        close(); identity.value = "";
        for (const mirror of Object.values(fallbacks)) if (field(mirror)) field(mirror).value = "";
        if (!settings.allowManualEntry) {
            name.value = "";
            if (boundName) boundName.value = "";
            for (const [, key] of selectionMappings) field(key).value = "";
            for (const input of [boundName || name, ...selectionMappings.map(([, key]) => field(key))]) changed(input);
        }
        changed(identity); lock();
        status.textContent = settings.allowManualEntry ? t("pickerUnlinked") : t("pickerCleared");
        status.classList.remove("app-picker-error"); name.focus();
    });
    find.addEventListener("click", () => {
        if (!editable()) return;
        if (popup && !popup.closed) { popup.focus(); return; }
        const address = new URL(popupUrl);
        address.searchParams.set("endpoint", endpoint.href);
        address.searchParams.set("minTermLength", settings.minTermLength ?? 2);
        address.searchParams.set("maxResults", settings.maxResults ?? 20);
        popup = window.open(address.href, "", "popup,width=680,height=620,resizable=yes,scrollbars=yes");
        status.textContent = popup ? "" : t("pickerAllowPopup");
        status.classList.toggle("app-picker-error", !popup);
    });
    loaded();
    return () => { lifetime.abort(); close(); };
}

export function mountAccountSearch({ keyOf = person => person.key, detailOf = person => person.detail,
    emptyMessage } = {}) {
    let translate = t;
    if (window.opener && !window.opener.closed) {
        try {
            translate = textForPage(window.opener.document);
            document.documentElement.lang = window.opener.document.documentElement.lang || document.documentElement.lang;
        } catch { /* A different origin cannot supply the form's language. */ }
    }
    for (const node of document.querySelectorAll("[data-picker-text]")) node.textContent = translate(node.dataset.pickerText);
    const query = document.querySelector("input"), results = document.querySelector("[data-results]");
    const message = document.querySelector("[data-search-status]");
    if (results.dataset.pickerLabel) results.setAttribute("aria-label", translate(results.dataset.pickerLabel));
    const parameters = new URL(location.href).searchParams;
    let endpoint;
    try { endpoint = new URL(parameters.get("endpoint"), location.origin); if (endpoint.origin !== location.origin || !parameters.get("endpoint")) throw new Error(); }
    catch { message.textContent = translate("pickerOpenFromForm"); return; }
    const minTermLength = Math.min(32, Math.max(2, Number(parameters.get("minTermLength")) || 2));
    const maxResults = Math.min(50, Math.max(1, Number(parameters.get("maxResults")) || 20));
    const minimum = translate("pickerMinLength", { count: minTermLength });
    document.getElementById("search-help").textContent = minimum;
    let timer, controller, generation = 0, composing = false;
    function stop() {
        clearTimeout(timer); controller?.abort(); controller = null; ++generation;
        results.removeAttribute("aria-busy");
    }
    window.addEventListener("pagehide", stop, { once: true });
    const dismiss = () => { window.opener?.focus(); window.close(); };
    document.querySelector("[data-cancel]").addEventListener("click", dismiss);
    document.addEventListener("keydown", event => {
        if (event.key === "Escape") { event.preventDefault(); dismiss(); }
    });
    async function search() {
        stop(); results.replaceChildren();
        const text = query.value.trim();
        if (text.length < minTermLength) { message.textContent = minimum; return; }
        const current = generation;
        controller = new AbortController();
        message.textContent = translate("pickerSearching"); results.setAttribute("aria-busy", "true");
        try {
            const response = await request(withQuery(endpoint, { s: text }), { signal: controller.signal });
            if (current !== generation) return;
            const people = response.items;
            for (const person of people) {
                const item = document.createElement("li"), choose = document.createElement("button");
                choose.type = "button"; choose.className = "app-directory-person";
                const label = document.createElement("span"); label.textContent = person.displayName; choose.append(label);
                const detail = detailOf(person);
                if (detail) { const secondary = document.createElement("small"); secondary.textContent = detail; choose.append(secondary); }
                choose.addEventListener("click", () => {
                    if (!window.opener || window.opener.closed) { message.textContent = translate("pickerClosed"); return; }
                    window.opener.postMessage({ type: selectionMessage, key: keyOf(person) }, location.origin);
                });
                item.append(choose); results.append(item);
            }
            if (people.length === 0) message.textContent = emptyMessage || translate("pickerEmpty");
            else if (people.length === maxResults) message.textContent = translate("pickerLimit", { count: maxResults });
            else message.textContent = translate(people.length === 1 ? "pickerOneMatch" : "pickerMatches", { count: people.length });
        } catch (error) {
            if (current !== generation || error.name === "AbortError") return;
            message.textContent = translate("pickerSearchFailed");
        } finally { if (current === generation) results.removeAttribute("aria-busy"); }
    }
    const changed = () => {
        stop(); results.replaceChildren();
        if (composing) return;
        message.textContent = query.value.trim().length < minTermLength ? minimum : translate("pickerSearching");
        if (query.value.trim().length >= minTermLength) timer = setTimeout(search, 300);
    };
    query.addEventListener("input", changed);
    query.addEventListener("compositionstart", () => { composing = true; stop(); });
    query.addEventListener("compositionend", () => { composing = false; changed(); });
    document.querySelector("[data-search-form]").addEventListener("submit", event => { event.preventDefault(); if (!composing) search(); });
    query.addEventListener("keydown", event => {
        if (event.key === "ArrowDown" && results.firstElementChild) { event.preventDefault(); results.querySelector("button").focus(); }
    });
    results.addEventListener("keydown", event => {
        const buttons = [...results.querySelectorAll("button")], index = buttons.indexOf(document.activeElement);
        if (index < 0) return;
        const next = event.key === "ArrowDown" ? Math.min(index + 1, buttons.length - 1) : event.key === "ArrowUp" ? index - 1
            : event.key === "Home" ? 0 : event.key === "End" ? buttons.length - 1 : null;
        if (next !== null) { event.preventDefault(); (next < 0 ? query : buttons[next]).focus(); }
    });
    query.focus();
    if (query.value) changed();
}
