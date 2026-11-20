import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { html, css, nothing } from "@umbraco-cms/backoffice/external/lit";
import { UMB_BLOCK_MANAGER_CONTEXT } from "@umbraco-cms/backoffice/block";
import { managementRequest } from "../AppBlocks/api.js";
import { values } from "./preview-model.js";

// A grid manager holds every block in the property. Only send the form being checked,
// in layout order, with the values the editor is currently viewing.
function formValue(layout, contents, culture) {
    const data = new Map(contents.map(block => [block.key, block]));
    const contentData = [];
    function visit(item) {
        const block = data.get(item.contentKey);
        if (!block) return false;
        const properties = Object.entries(values(block, culture)).map(([alias, value]) => ({ alias, value }));
        contentData.push({ key: block.key, contentTypeKey: block.contentTypeKey, values: properties });
        return (item.areas ?? []).every(area => (area.items ?? []).every(visit));
    }
    if (layout && visit(layout)) return { layout: { "Umbraco.BlockGrid": [layout] }, contentData, settingsData: [], expose: [] };
}

export class FormProblems extends UmbLitElement {
    static properties = { layout: { attribute: false }, culture: {}, contents: { state: true },
        paths: { attribute: false }, problems: { state: true }, failed: { state: true } };
    #timer;
    #generation = 0;
    #last;
    constructor() {
        super();
        this.consumeContext(UMB_BLOCK_MANAGER_CONTEXT, manager =>
            this.observe(manager?.contents, contents => this.contents = contents, "form-content"));
    }
    updated(changes) {
        if (["layout", "culture", "contents"].some(key => changes.has(key))) this.#queue();
    }
    connectedCallback() { super.connectedCallback(); this.#queue(); }
    disconnectedCallback() {
        clearTimeout(this.#timer);
        this.#generation++;
        this.#last = undefined;
        super.disconnectedCallback();
    }
    #queue() {
        clearTimeout(this.#timer);
        const generation = ++this.#generation;
        this.#timer = setTimeout(async () => {
            const body = formValue(this.layout, this.contents ?? [], this.culture);
            if (!body) { this.problems = []; this.#last = undefined; return; }
            const snapshot = JSON.stringify(body);
            if (this.#last === snapshot) return;
            this.#last = undefined;
            this.problems = [];
            this.failed = false;
            try {
                const problems = await managementRequest("application-blocks/validate-form", { method: "POST", body });
                if (generation !== this.#generation) return;
                this.#last = snapshot;
                this.problems = problems;
            } catch {
                if (generation === this.#generation) { this.#last = undefined; this.failed = true; }
            }
        }, 350);
    }
    render() {
        if (this.failed) return html`<p>Form validation is unavailable. <uui-button compact label="Retry validation" @click=${this.#queue}>Retry</uui-button></p>`;
        if (!this.problems?.length) return nothing;
        return html`<section aria-label="Form validation" aria-live="polite"><strong>Form needs attention</strong>
            <ul>${this.problems.map(problem => html`<li>${problem.message}
                <ul>${problem.locations.map(target => html`<li>${this.paths?.get(target.key)
                    ? html`<a href=${this.paths.get(target.key)}>${target.label}</a>` : target.label}</li>`)}</ul>
            </li>`)}</ul></section>`;
    }
    static styles = css`
        section,p{margin:var(--uui-size-space-3);padding:var(--uui-size-space-3);border-inline-start:3px solid var(--uui-color-danger)}
        ul{margin:.35em 0;padding-inline-start:1.4em}a{color:var(--uui-color-interactive)}
    `;
}
customElements.define("app-form-problems", FormProblems);
