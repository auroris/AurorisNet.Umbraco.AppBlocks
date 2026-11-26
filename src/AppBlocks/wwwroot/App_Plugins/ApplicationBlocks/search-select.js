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
    const rawMax = select.dataset.maxOptions;
    const parsedMax = rawMax === undefined || rawMax === "" ? 50 : Number(rawMax);
    const maxOptions = Number.isInteger(parsedMax) && parsedMax >= 0 ? (parsedMax === 0 ? null : parsedMax) : 50;
    // Tom Select indexes options on a plain object. Prefix keys so any database value can be selected.
    const optionKey = value => ":" + value;
    for (const option of select.options) option.value = optionKey(option.value);
    select.selectedIndex = -1;
    const picker = new TomSelect(select, {
        placeholder: select.dataset.placeholder,
        plugins: { remove_button: { title: t("removeChoice") } },
        create: false,
        maxOptions,
        delimiter: "", // Treat pasted text, including commas, as a search.
        persist: false,
        render: { no_results: (_, escape) => `<div class="no-results">${escape(t("noMatchingChoices"))}</div>` },
    });
    const control = picker.control_input;
    select.setAttribute("aria-hidden", "true");
    const describedBy = select.getAttribute("aria-describedby");
    if (describedBy) control.setAttribute("aria-describedby", describedBy);
    let problem = "";

    function refresh() {
        if (field.disabled !== picker.isDisabled) {
            if (field.disabled) picker.disable();
            else picker.enable();
        }
        const required = root.dataset.required === "true";
        control.setAttribute("aria-required", String(required));
        control.setCustomValidity(problem || (required && !picker.items.length ? t(multiple ? "shuttleRequired" : "chooseAvailable") : ""));
    }
    function display(record) {
        picker.clear(true);
        problem = "";
        try {
            const values = multiple ? parseShuttleValue(field.value) : field.value ? [field.value] : [];
            const selected = [];
            for (const value of values) {
                const key = optionKey(value);
                if (!Object.hasOwn(picker.options, key)) {
                    if (!record.key) { problem = t("chooseAvailable"); continue; }
                    // Only this record can keep a retired value. Removing it also removes the temporary option.
                    picker.addOption({ value: key, text: t("currentValue", { value }) }, true);
                }
                selected.push(key);
            }
            picker.addItems(selected, true);
        } catch (error) { problem = error.message; }
        refresh();
    }
    picker.on("change", () => {
        const values = picker.items.map(key => key.slice(1));
        problem = "";
        field.value = multiple ? shuttleCsv(values) : values[0] ?? "";
        refresh();
        field.dispatchEvent(new Event("input", { bubbles: true }));
        field.dispatchEvent(new Event("change", { bubbles: true }));
    });
    return { control, display, refresh, dispose: () => picker.destroy() };
}
