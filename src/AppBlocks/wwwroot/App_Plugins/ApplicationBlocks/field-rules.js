// Form-rule values have the same wire types as form submissions. Keep decimal comparisons exact.
function decimal(value) {
    const parts = /^([+-]?)(\d*)(?:\.(\d*))?(?:e([+-]?\d+))?$/i.exec(value.trim());
    if (!parts || !(parts[2] || parts[3])) return null;
    const scale = (parts[3]?.length ?? 0) - Number(parts[4] ?? 0);
    if (Math.abs(scale) > 100) return null;
    return { digits: BigInt((parts[1] || "+") + parts[2] + (parts[3] || "")), scale };
}

export function matchesCondition(c, values) {
    let a = String(values.get(c.field) ?? ""), b = String(c.otherField ? values.get(c.otherField) ?? "" : c.literal);
    if (c.comparison === "Is empty") return !a.trim();
    if (c.comparison === "Has a value") return !!a.trim();
    if (!a.trim() || !b.trim()) {
        if (!["Is", "Is not"].includes(c.comparison)) return false;
        a = a.trim(); b = b.trim();
    } else if (c.kind === "number") {
        const x = decimal(a), y = decimal(b);
        if (!x || !y) return false;
        const scale = Math.max(x.scale, y.scale);
        a = x.digits * 10n ** BigInt(scale - x.scale);
        b = y.digits * 10n ** BigInt(scale - y.scale);
    } else if (c.kind === "datetime") {
        a = Date.parse(a); b = Date.parse(b);
        if (!Number.isFinite(a) || !Number.isFinite(b)) return false;
    } else if (c.kind === "time") {
        const time = value => {
            const match = /^(\d{2}):(\d{2})(?::(\d{2})(?:\.(\d+))?)?$/.exec(value);
            return match && match[1] + match[2] + (match[3] || "00") + (match[4] || "").padEnd(7, "0");
        };
        a = time(a); b = time(b);
        if (!a || !b) return false;
    }
    switch (c.comparison) {
        case "Is": return a === b;
        case "Is not": return a !== b;
        case "Is greater than": return a > b;
        case "Is at least": return a >= b;
        case "Is less than": return a < b;
        case "Is at most": return a <= b;
        default: return false;
    }
}

export function mountFieldRules(form, fields, shuttles, colors, readValue) {
    const rules = JSON.parse(form.dataset.fieldRules || "[]");
    if (!rules.length) return () => {};
    const controls = new Map(fields.map(field => [field.dataset.field, field]));
    const groups = Map.groupBy(rules, rule => rule.field);
    const fixed = new Map(fields.map(field => [field, field.required || field.closest("[data-app-shuttle]")?.dataset.required === "true"]));
    let record;
    function apply(current) {
        record = current;
        if (!record) return;
        const values = new Map(fields.map(field => [field.dataset.field, readValue(field)]));
        const matches = rule => rule.match === "Any"
            ? rule.conditions.some(c => matchesCondition(c, values))
            : rule.conditions.every(c => matchesCondition(c, values));
        const done = new Set();
        function update(name) {
            if (done.has(name) || !groups.has(name)) return;
            done.add(name);
            const field = controls.get(name);
            if (!field) return;
            const rules = groups.get(name);
            for (const rule of rules.filter(r => ["Show", "Hide", "Make read only"].includes(r.action)))
                for (const c of rule.conditions) { update(c.field); update(c.otherField); }
            const active = action => rules.some(r => r.action === action && matches(r));
            const hidden = rules.some(r => r.action === "Show" && !matches(r)) || active("Hide");
            const disabled = hidden || active("Make read only") || field.dataset.readonly === "true" || !!record.readOnlyFields?.includes(name)
                || !(record.key ? record.capabilities.update : record.capabilities.create);
            const cell = field.closest(".app-form-field")?.closest(".ah-grid__item") ?? field.closest(".app-form-field");
            if (cell) cell.hidden = hidden;
            field.disabled = disabled;
            if (disabled) values.set(name, record.values[name]);
        }
        for (const name of groups.keys()) update(name);
        for (const [name, rules] of groups) {
            const field = controls.get(name);
            if (!field) continue;
            const required = fixed.get(field) || rules.some(r => r.action === "Require" && matches(r));
            field.required = required && field.type !== "checkbox";
            const root = field.closest("[data-app-shuttle]");
            if (root) { root.dataset.required = String(required); shuttles.get(field)?.refresh(); }
            colors.get(field)?.display();
            const marker = field.closest(".app-form-field")?.querySelector("[data-required-mark]");
            if (marker) marker.hidden = !required;
            const message = !field.disabled && String(values.get(name) ?? "").trim() && rules.find(r => r.action === "Validate" && !matches(r))?.message;
            if (!root || message) (shuttles.get(field)?.control ?? field).setCustomValidity(message || "");
        }
    }
    form.addEventListener("input", () => apply(record));
    form.addEventListener("change", () => apply(record));
    return apply;
}
