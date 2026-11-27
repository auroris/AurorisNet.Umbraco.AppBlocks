import { html } from "@umbraco-cms/backoffice/external/lit";
import { UMB_DOCUMENT_WORKSPACE_CONTEXT } from "@umbraco-cms/backoffice/document";
import { AppEditor } from "./editor-base.js";
import { enclosingWorkspace } from "./workspace-context.js";
import { managementRequest } from "../AppBlocks/api.js";

export default class ConditionItem extends AppEditor {
    static properties = { ...AppEditor.properties, properties: { state: true } };
    async connectedCallback() {
        super.connectedCallback();
        const workspace = await enclosingWorkspace(this, UMB_DOCUMENT_WORKSPACE_CONTEXT);
        this.observe(workspace?.data, async data => {
            const type = data?.documentType?.unique;
            if (!type || type === this.type) return;
            this.type = type;
            try { this.properties = await managementRequest("application-blocks/page-properties/" + type); }
            catch (error) { this.error = error.message; }
        }, "document-type");
    }
    render() {
        const picked = /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i.test(this.value ?? "");
        const options = (this.properties ?? []).map(p => ({ value: p.key, label: p.name + " (" + p.alias + ")" }));
        return html`<select aria-label="Page property or form field" ?disabled=${this.readonly} @change=${event => this.change(event.target.value)}>
            <option value="" ?selected=${!picked}>Form field</option>
            ${picked && !options.some(o => o.value === this.value) ? html`<option selected value=${this.value}>Unavailable property (${this.value})</option>` : ""}
            <optgroup label="Page properties">${options.map(o => html`<option value=${o.value} ?selected=${o.value === this.value}>${o.label}</option>`)}</optgroup>
        </select>
        ${!picked ? html`<uui-input label="Field key" .value=${this.value ?? ""} ?readonly=${this.readonly}
            @input=${event => this.change(event.target.value)}></uui-input>` : ""}
        ${this.error ? html`<p role="alert">${this.error}</p>` : ""}`;
    }
}
customElements.define("app-condition-item", ConditionItem);
