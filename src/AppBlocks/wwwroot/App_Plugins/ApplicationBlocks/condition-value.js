import { html } from "@umbraco-cms/backoffice/external/lit";
import { UMB_BLOCK_ELEMENT_PROPERTY_DATASET_CONTEXT } from "@umbraco-cms/backoffice/block";
import { AppEditor } from "./editor-base.js";
import { request } from "../AppBlocks/api.js";

export default class ConditionValue extends AppEditor {
    static properties = { ...AppEditor.properties, comparison: { state: true }, groups: { state: true } };
    constructor() {
        super();
        this.consumeContext(UMB_BLOCK_ELEMENT_PROPERTY_DATASET_CONTEXT, async dataset => {
            this.observe(await dataset?.propertyValueByAlias("comparison"), async value => {
                this.comparison = value;
                if (value === "Backoffice user is in group" && !this.groups) {
                    try { this.groups = await request("user-groups"); }
                    catch (error) { this.error = error.message; }
                }
            }, "comparison");
        });
    }
    render() {
        if (this.comparison === "Backoffice user is in group") {
            const selected = this.groups?.find(g => g.alias === this.value)?.key ?? this.value;
            return this.renderChoices((this.groups ?? []).map(g => ({ value: g.key, label: g.name })), "Backoffice group", selected);
        }
        return html`<uui-input label="Value" .value=${this.value ?? ""} ?readonly=${this.readonly}
            @input=${event => this.change(event.target.value)}></uui-input>`;
    }
}
customElements.define("app-condition-value", ConditionValue);
