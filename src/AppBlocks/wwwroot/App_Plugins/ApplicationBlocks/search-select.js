import TomSelect from "./tom-select.js";
import { parseShuttleValue, shuttleCsv } from "./field-controls.js";
import { t } from "./runtime-text.js";

// A panel can fetch a form for a different record whose fields contributed no styles to the initial page.
// Insert before runtime.css so AppBlocks tokens and site theme rules win the cascade.
const stylesheet = new URL("./tom-select.css", import.meta.url).href;
if (![...document.querySelectorAll('link[rel="stylesheet"]')].some(link => link.href === stylesheet)) {
    const link = document.createElement("link");
    link.rel = "stylesheet";
    link.href = stylesheet;
    const runtime = [...document.querySelectorAll('link[rel="stylesheet"]')].find(item => item.href.endsWith("/runtime.css"));
    if (runtime) runtime.before(link);
    else document.head.prepend(link);
}

export function mountSearchSelect(root) {
    const field = root.querySelector("[data-field]");
    const select = root.querySelector("select");
    const multiple = select.multiple;
    const freeText = select.dataset.allowFreeText === "true";
    const rawMax = select.dataset.maxOptions;
    const parsedMax = rawMax === undefined || rawMax === "" ? 50 : Number(rawMax);
    const maxOptions = Number.isInteger(parsedMax) && parsedMax >= 0 ? (parsedMax === 0 ? null : parsedMax) : 50;
    // Tom Select indexes options on a plain object. Prefix keys so any database value can be selected.
    const optionKey = value => ":" + value;
    const normalizeLabel = text => text.trim().toLowerCase();
    const suggestedKeys = new Map();
    for (const option of select.options) {
        option.value = optionKey(option.value);
        // An exact, unambiguous label typed on blur should save the same key as clicking the suggestion.
        const label = normalizeLabel(option.text);
        suggestedKeys.set(label, suggestedKeys.has(label) ? null : option.value);
    }
    select.selectedIndex = -1;
    const picker = new TomSelect(select, {
        placeholder: select.dataset.placeholder,
        plugins: { remove_button: { title: t("removeChoice") } },
        create: freeText && function (input) {
            if (field.readOnly || field.matches(":disabled")) return false;
            const text = input.trim();
            if (!text) return false;
            const key = optionKey(text);
            return this.options[suggestedKeys.get(normalizeLabel(text))] ?? this.options[key] ?? { value: key, text };
        },
        createFilter: input => !!input.trim(),
        maxOptions,
        delimiter: "", // Treat pasted text, including commas, as a search.
        persist: false,
        render: {
            no_results: ({ input }, escape) => freeText && input.trim() ? null : `<div class="no-results">${escape(t("noMatchingChoices"))}</div>`,
            option_create: ({ input }, escape) => {
                const text = input.trim();
                if (!text || suggestedKeys.get(normalizeLabel(text))) return null;
                return `<div class="create" role="option" id="${escape(select.id)}-create">${escape(t("useTypedChoice", { value: text }))}</div>`;
            },
        },
    });
    const control = picker.control_input;
    // Tom Select defers focus when a label opens the picker. The input can already contain
    // text and lose focus before that callback runs. Commit before its blur handler closes it.
    function commitTypedValue() {
        if (document.hasFocus() && picker.inputValue()) picker.createItem();
    }
    if (freeText) control.addEventListener("blur", commitTypedValue, true);
    const maxLength = Number(select.dataset.maxLength);
    if (!multiple && freeText && Number.isInteger(maxLength) && maxLength > 0) control.maxLength = maxLength;
    select.setAttribute("aria-hidden", "true");
    const describedBy = select.getAttribute("aria-describedby");
    if (describedBy) control.setAttribute("aria-describedby", describedBy);
    let problem = "";
    let displayedValue;

    function syncLock() {
        if (field.disabled !== picker.isDisabled) {
            if (field.disabled) picker.disable();
            else picker.enable();
        }
        if (field.readOnly !== picker.isReadOnly) {
            if (field.readOnly) picker.close();
            picker.setReadOnly(field.readOnly);
        }
    }
    function refresh() {
        syncLock();
        const required = root.dataset.required === "true";
        control.setAttribute("aria-required", String(required));
        const requiredMessage = freeText ? "enterOrSelect" : multiple ? "shuttleRequired" : "chooseAvailable";
        control.setCustomValidity(problem || (required && !picker.items.length ? t(requiredMessage) : ""));
    }
    function displayValue(stored) {
        displayedValue = field.value;
        picker.clear(true);
        picker.setTextboxValue("");
        problem = "";
        try {
            const values = multiple ? parseShuttleValue(field.value) : field.value ? [field.value] : [];
            const selected = [];
            for (const value of values) {
                const key = optionKey(value);
                if (!Object.hasOwn(picker.options, key)) {
                    if (!freeText && !stored) { problem = t("chooseAvailable"); continue; }
                    picker.addOption({ value: key, text: freeText ? value : t("currentValue", { value }) }, true);
                }
                selected.push(key);
            }
            picker.addItems(selected, true);
        } catch (error) { problem = error.message; }
        refresh();
    }
    function fieldChanged() {
        if (field.value !== displayedValue) displayValue(false);
        else syncLock();
    }
    field.addEventListener("change", fieldChanged);
    const locks = new MutationObserver(syncLock);
    locks.observe(field, { attributes: true, attributeFilter: ["readonly", "disabled"] });
    picker.on("change", () => {
        const values = picker.items.map(key => key.slice(1));
        problem = "";
        field.value = multiple ? shuttleCsv(values) : values[0] ?? "";
        displayedValue = field.value;
        refresh();
        field.dispatchEvent(new Event("input", { bubbles: true }));
        field.dispatchEvent(new Event("change", { bubbles: true }));
    });
    return {
        control, display: record => displayValue(!!record.key), refresh,
        dispose() {
            locks.disconnect();
            field.removeEventListener("change", fieldChanged);
            control.removeEventListener("blur", commitTypedValue, true);
            picker.destroy();
        },
    };
}
