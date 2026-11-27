import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { html, css, nothing } from "@umbraco-cms/backoffice/external/lit";
import { DOMPurify } from "@umbraco-cms/backoffice/external/dompurify";
import { UMB_PROPERTY_DATASET_CONTEXT } from "@umbraco-cms/backoffice/property";
import { UmbMediaDetailRepository } from "@umbraco-cms/backoffice/media";
import { codeSize, escape, json, values, note, renderAppBlock } from "./preview-model.js";
import { appBlockTypes } from "./block-types.js";
import { pathBase } from "./data-api.js";
import "@umbraco-cms/backoffice/block-grid";
import { UMB_BLOCK_LIST } from "@umbraco-cms/backoffice/block-list";
import { UMB_BLOCK_SINGLE } from "@umbraco-cms/backoffice/block-single";
import "@umbraco-cms/backoffice/ufm";
import "./form-problems.js";

// The site root under any path base.
const siteRoot = new URL(`${pathBase}/`, import.meta.url);
// Stylesheet URLs loaded into every preview, in order. A site's appEntryPoint adds its frontend stylesheet here
// once at backoffice startup, so previews look like the page.
export const previewStylesheets = [new URL("runtime.css", import.meta.url).pathname];
// CSS text added after the stylesheets and the preview's own rules. A site's appEntryPoint adds preview-only
// overrides here, such as showing every panel of a component that the page reveals with script.
export const previewStyles = [];
// A companion's blocks can also occur inside the Forms container's nested grid: content type key OR alias -> (content, settings) => markup.
export const nestedBlockPreviews = new Map();
// Maps element content type key (lowercase GUID) -> element alias, populated from AppBlocks.BlockTypeAliases.
export const nestedBlockAliases = new Map();
function columnsPerArea(alias) {
    switch (alias) {
        case "appColumns": case "appFormColumns": return 6;
        case "appThreeColumns": case "appFormThreeColumns": return 4;
        default: return 12;
    }
}

export class BlockPreview extends UmbLitElement {
    static properties = Object.fromEntries(["content", "settings", "layout", "blockType", "config", "manifest", "label", "icon", "index", "unpublished", "readonly"].map(name => [name, { attribute: false }]));
    #media = new Map();
    #mediaRepository = new UmbMediaDetailRepository(this);
    #resize;
    #dataset;
    #regionKey;
    #folded = false;
    #paths;
    get culture() { return this.#dataset?.getVariantId?.()?.culture; }
    constructor() {
        super();
        this.consumeContext(UMB_PROPERTY_DATASET_CONTEXT, context => {
            this.#dataset = context;
            this.requestUpdate();
        });
    }
    get alias() { return this.manifest?.forContentTypeAlias; }
    get stylesheets() { return previewStylesheets; }
    get styleOverrides() { return previewStyles; }
    renderContent(alias, content, settings) {
        return renderAppBlock(alias, content, settings, value => this.nestedGrid(value), this.culture);
    }
    nestedGrid(raw) {
        const grid = json(raw);
        const items = grid?.layout?.["Umbraco.BlockGrid"] ?? [];
        const render = (entries, columns = 12) => entries.map(entry => {
            const data = grid.contentData?.find(item => item.key === entry.contentKey);
            const type = appBlockTypes.find(type => type.key === data?.contentTypeKey);
            const alias = type?.alias ?? (typeof data?.contentTypeKey === "string" ? nestedBlockAliases.get(data.contentTypeKey.toLowerCase()) : undefined);
            const companion = nestedBlockPreviews.get(data?.contentTypeKey) ?? (alias ? nestedBlockPreviews.get(alias) : undefined);
            const settings = values(grid.settingsData?.find(item => item.key === entry.settingsKey), this.culture);
            const areas = entry.areas ?? [];
            const areaColumns = columnsPerArea(alias);
            const split = areaColumns === 6 ? ({ "4/8": "1fr 2fr", "8/4": "2fr 1fr" }[settings.split] ?? "1fr 1fr") : `repeat(${Math.max(1, areas.length)},minmax(0,1fr))`;
            const children = areas.length ? `<div style="display:grid;grid-template-columns:${split};gap:10px">${areas.map(area => `<div class="preview-nested-grid" style="grid-template-columns:repeat(${areaColumns},minmax(0,1fr))">${render(area.items ?? [], areaColumns)}</div>`).join("")}</div>` : "";
            const content = values(data, this.culture);
            const body = companion ? companion(content, settings) : this.renderContent(alias, content, settings);
            return `<section class="preview-nested" style="grid-column:span ${Math.max(1, Math.min(columns, entry.columnSpan || columns))}">${body}${children}</section>`;
        }).join("");
        return items.length ? `<div class="preview-nested-grid">${render(items)}</div>` : "";
    }
    mediaImage(raw, alt = "", caption = "", className = "") {
        const value = json(raw);
        const key = Array.isArray(value) ? value[0]?.mediaKey : value?.mediaKey;
        let url = typeof value === "string" && value.startsWith("/") && !value.startsWith("//") ? value : value?.src;
        if (key) {
            if (!this.#media.has(key)) {
                this.#media.set(key, null);
                this.#mediaRepository.requestByUnique(key).then(({ data }) => {
                    const file = json(data?.values?.find(value => value.alias === "umbracoFile")?.value);
                    this.#media.set(key, typeof file === "string" ? file : file?.src);
                    this.requestUpdate();
                }).catch(() => { this.#media.set(key, undefined); this.requestUpdate(); });
            }
            url = this.#media.get(key);
        }
        return url ? `<figure class="${escape(className)}"><img src="${escape(url)}" alt="${escape(alt)}">${caption ? `<figcaption>${escape(caption)}</figcaption>` : ""}</figure>` : note(key ? "Image unavailable or loading." : "Choose an image.");
    }
    previewDocument(markup) {
        // Umbraco's own sanitizer plus an inert document: raw HTML can never execute in the backoffice.
        const safe = DOMPurify.sanitize(markup, { FORCE_BODY: true, FORBID_TAGS: ["script", "iframe", "object", "embed", "form", "meta", "base", "link"], FORBID_ATTR: ["autofocus", "srcdoc"] });
        // Use the site's explicit origin: Firefox can resolve 'self' against about:srcdoc in a meta CSP.
        // Allow same-origin connect-src so browser DevTools can fetch stylesheet source maps without CSP console errors.
        const origin = escape(siteRoot.origin);
        return `<!doctype html><html><head><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'none'; object-src 'none'; frame-src 'none'; img-src ${origin} data:; style-src ${origin} 'unsafe-inline'; font-src ${origin}; connect-src ${origin}; form-action 'none'; base-uri ${origin}"><base href="${escape(siteRoot.href)}">${this.stylesheets.map(url => `<link rel="stylesheet" href="${escape(url)}">`).join("")}<style>
html,body{margin:0;padding:0;background:white;color:#24323d;font:14px/1.5 system-ui,sans-serif}body{padding:16px;box-sizing:border-box;overflow-wrap:anywhere}h1,h2,h3,p,figure{margin:0 0 12px}h3{font-size:19px}img{max-width:100%;height:auto;max-height:320px;object-fit:contain}table{width:100%;border-collapse:collapse}td,th{border:1px solid #d9dfe3;padding:8px;text-align:left}th{background:#f2f5f7}label{display:block;font-weight:600}input:not([type=checkbox]),textarea,select{box-sizing:border-box;max-width:100%;width:100%;padding:8px;border:1px solid #b7c4cd;border-radius:3px;background:#fff;color:#334}input[type=checkbox]{width:auto}button{padding:7px 12px;margin:4px;border:1px solid #9aafbf;border-radius:3px;background:#eef3f6;color:#334}small,.preview-note{display:block;color:#586c7a;font-size:12px}.preview-note{margin:7px 0}pre{white-space:pre-wrap;max-height:180px;overflow:hidden;background:#f4f6f8;padding:12px}dt{font-weight:600}dd{margin:0 0 8px}.preview-dropzone{border:2px dashed #c8d3db;padding:14px;text-align:center}.preview-columns{display:flex;gap:12px}.preview-columns>*{flex:1;min-width:0}.preview-timeline{display:grid;grid-template-columns:1fr 3fr;gap:1px;background:#d5dfe6}.preview-timeline>div{background:#f3f6f8;padding:10px}.preview-bar{display:block;background:#c3dbe9;padding:6px;border-radius:4px;margin-left:15%;width:55%}.preview-nested-grid{display:grid;grid-template-columns:repeat(12,minmax(0,1fr));gap:10px}.preview-nested{border:1px solid #d9e1e6;padding:10px;min-width:0}
</style>${this.styleOverrides.length ? `<style>${this.styleOverrides.join("\n").replace(/<\/style/gi, "<\\/style")}</style>` : ""}</head><body>${safe}</body></html>`;
    }
    #loaded(event) {
        this.#resize?.disconnect();
        const frame = event.target;
        const body = frame.contentDocument?.body;
        if (!body) return;
        const resize = () => { frame.style.height = `${Math.max(48, body.getBoundingClientRect().height + 2)}px`; };
        this.#resize = new ResizeObserver(resize);
        this.#resize.observe(body);
        resize();
    }
    disconnectedCallback() { this.#resize?.disconnect(); super.disconnectedCallback(); }
    willUpdate(changed) {
        super.willUpdate(changed);
        const key = this.alias === "appFormRegion" && this.layout?.contentKey;
        if (key !== this.#regionKey) {
            this.#regionKey = key;
            this.#folded = false;
            try { if (key) this.#folded = sessionStorage.getItem(`AppBlocks:${pathBase}:region:${key}`) === "folded"; } catch {}
        }
    }
    #toggleRegion() {
        this.#folded = !this.#folded;
        if (this.#regionKey) {
            const key = `AppBlocks:${pathBase}:region:${this.#regionKey}`;
            try {
                if (this.#folded) sessionStorage.setItem(key, "folded"); else sessionStorage.removeItem(key);
            } catch { /* Folding also works when the browser disables session storage. */ }
        }
        this.requestUpdate();
    }
    updated(changes) {
        super.updated(changes);
        if (changes.has("config") || changes.has("layout") || changes.has("manifest")) {
            const key = this.layout?.contentKey;
            const path = this.config?.showContentEdit ? this.config.editContentPath : undefined;
            if (key) {
                if (this.alias === "appForm") this.#rememberPath(key, path);
                this.dispatchEvent(new CustomEvent("app-block-edit-path", { bubbles: true, composed: true, detail: { key, path } }));
            }
        }
    }
    #rememberPath(key, path) {
        this.#paths ??= new Map();
        if (this.#paths.get(key) === path) return;
        if (path) this.#paths.set(key, path); else this.#paths.delete(key);
        this.shadowRoot.querySelector("app-form-problems")?.requestUpdate();
    }
    #childPath(event) {
        event.stopPropagation();
        this.#rememberPath(event.detail.key, event.detail.path);
    }
    render() {
        const size = codeSize(this.alias, this.content);
        const markup = size ? "" : this.renderContent(this.alias, this.content ?? {}, this.settings ?? {});
        const edit = this.config?.showContentEdit ? this.config.editContentPath : undefined;
        const split = columnsPerArea(this.alias) === 6 ? ({ "4/8": "1fr 2fr", "8/4": "2fr 1fr" }[this.settings?.split] ?? "1fr 1fr") : undefined;
        const grid = ![UMB_BLOCK_LIST, UMB_BLOCK_SINGLE].includes(this.manifest?.forBlockEditor);
        return html`<div class="block" @app-block-edit-path=${this.alias === "appForm" ? this.#childPath : nothing}>
            <div class="header">
                ${this.alias === "appFormRegion" && grid ? html`<button class="fold" type="button"
                    aria-label=${`${this.#folded ? "Expand" : "Collapse"} ${this.content?.heading || "form region"}`}
                    aria-expanded=${String(!this.#folded)} aria-controls="region-content" @click=${this.#toggleRegion}>
                    <umb-icon .name=${this.#folded ? "icon-navigation-right" : "icon-navigation-down"}></umb-icon>
                </button>` : nothing}
                ${this.#header(edit, size)}
            </div>
            ${this.alias === "appForm" && grid ? html`<app-form-problems .layout=${this.layout} .culture=${this.culture} .paths=${this.#paths ??= new Map()}></app-form-problems>` : nothing}
            <div id="region-content" ?hidden=${this.#folded}>
            <!-- Script/object/frame/form execution is blocked by DOMPurify + meta CSP + inert; omitting sandbox="allow-same-origin" avoids Edge DevTools spamming "Blocked script execution in 'about:srcdoc'" on every preview. -->
            ${markup ? html`<div class="preview"><iframe title="Block content preview" inert tabindex="-1" .srcdoc=${this.previewDocument(markup)} @load=${this.#loaded}></iframe>${edit ? html`<a class="edit-preview" href=${edit} draggable="false" aria-label="Edit block content"></a>` : nothing}</div>` : nothing}
            ${grid ? html`<umb-block-grid-areas-container class=${split ? "split" : ""} style=${split ? `--preview-columns:${split}` : ""} draggable="false"></umb-block-grid-areas-container>` : nothing}
            </div>
        </div>`;
    }
    // The block editor's own header for the editor the manifest names. Each element ships with the editor that hosts the view.
    #header(edit, size) {
        const value = { ...this.content, $settings: this.settings, $index: this.index };
        const label = size ? `${this.label || (this.alias === "appCss" ? "CSS" : "JavaScript")} · ${size}` : this.label;
        switch (this.manifest?.forBlockEditor) {
            case UMB_BLOCK_LIST: return html`<umb-ref-list-block .icon=${this.icon} .index=${this.index} .unpublished=${this.unpublished} .config=${this.config} .content=${this.content} .settings=${this.settings}>
                <umb-ufm-render slot="name" inline .markdown=${label} .value=${value}></umb-ufm-render>
            </umb-ref-list-block>`;
            case UMB_BLOCK_SINGLE: return html`<umb-ref-single-block .label=${label} .icon=${this.icon} .index=${this.index} .unpublished=${this.unpublished} .config=${this.config} .content=${this.content} .settings=${this.settings}></umb-ref-single-block>`;
            default: return html`<umb-ref-grid-block standalone .readonly=${!edit} .href=${edit}>
                <umb-icon slot="icon" .name=${this.icon}></umb-icon>
                <umb-ufm-render slot="name" inline .markdown=${label} .value=${value}></umb-ufm-render>
                ${this.unpublished ? html`<uui-tag slot="name" look="secondary">Not published</uui-tag>` : nothing}
            </umb-ref-grid-block>`;
        }
    }
    static styles = css`
        .header{display:flex;align-items:center}.header>:last-child{flex:1;min-width:0}
        .fold{margin-inline-start:4px;padding:8px;border:0;border-radius:var(--uui-border-radius);background:transparent;color:var(--uui-color-interactive);cursor:pointer}
        .fold:hover,.fold:focus-visible{background:var(--uui-color-surface-emphasis)}
        :host{display:block;height:100%;min-width:0}.block{background:var(--uui-color-surface);border:1px solid var(--uui-color-border);border-radius:4px}umb-ref-grid-block{border:0;min-height:38px}umb-ref-list-block,umb-ref-single-block{margin:0}umb-ufm-render{user-select:none}.preview{position:relative}iframe{display:block;width:100%;height:80px;border:0;pointer-events:none}.edit-preview{position:absolute;inset:0;display:block}.edit-preview:focus-visible{outline:2px solid var(--uui-color-interactive);outline-offset:-2px}umb-block-grid-areas-container::part(area){margin:8px;min-width:0}umb-block-grid-areas-container.split::part(area-container){display:grid;grid-template-columns:var(--preview-columns)}umb-block-grid-areas-container.split::part(area){grid-column:auto!important}uui-tag{margin-left:8px}
    `;
}
customElements.define("app-block-preview", BlockPreview);
export default BlockPreview;
