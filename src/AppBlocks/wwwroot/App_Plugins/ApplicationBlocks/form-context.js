// Block grids keep content in a flat collection and nesting in layout areas. A field belongs to its
// enclosing form, which can be any of several forms on a website page.
import { appBlockTypes } from "./block-types.js";
import { json, values } from "./preview-model.js";

const textType = appBlockTypes.find(type => type.alias === "appText").key;
const formType = appBlockTypes.find(type => type.alias === "appForm").key;

export function enclosingForm(contents, layouts, contentKey, parentKey) {
    if (!contentKey) return undefined;
    const data = new Map((contents ?? []).map(content => [content.key, content]));
    function find(items, key, owner) {
        for (const item of items ?? []) {
            const block = data.get(item.contentKey);
            const current = block?.contentTypeKey === formType || block?.values?.some(v => v.alias === "formInterface") ? item : owner;
            if (item.contentKey === key) return { owner: current };
            for (const area of item.areas ?? []) {
                const found = find(area.items, key, current);
                if (found) return found;
            }
        }
        return undefined;
    }
    // A newly created block is still local to its editor until Submit.
    const form = (find(layouts, contentKey) ?? find(layouts, parentKey))?.owner;
    if (!form) return;
    const resource = values(json(values(data.get(form.contentKey)).formInterface)?.contentData?.[0]).resource;
    return { resource, layout: form, data };
}

export function enclosingFormResource(contents, layouts, contentKey, parentKey) {
    return enclosingForm(contents, layouts, contentKey, parentKey)?.resource;
}

export function writableTextBindings(form) {
    const bindings = new Set();
    function visit(item) {
        const block = form.data.get(item.contentKey);
        if (item !== form.layout && block?.contentTypeKey === formType) return;
        if (block?.contentTypeKey === textType) {
            const { field, readOnly } = values(block);
            if (field && ![true, 1, "1", "true"].includes(readOnly)) bindings.add(field);
        }
        for (const area of item.areas ?? []) for (const child of area.items ?? []) visit(child);
    }
    if (form) visit(form.layout);
    return bindings;
}
