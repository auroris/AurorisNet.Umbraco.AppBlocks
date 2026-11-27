import { AppEditor } from "./editor-base.js";
import { UMB_BLOCK_MANAGER_CONTEXT, UMB_BLOCK_ENTRIES_CONTEXT, UMB_BLOCK_ELEMENT_PROPERTY_DATASET_CONTEXT } from "@umbraco-cms/backoffice/block";
import { html } from "@umbraco-cms/backoffice/external/lit";
import { enclosingForm } from "./form-context.js";
import { listItems } from "./preview-model.js";
export default class FieldPicker extends AppEditor {
    static properties = { ...AppEditor.properties, form: { state: true }, localResource: { state: true } };
    constructor() {
        super();
        this.consumeContext(UMB_BLOCK_MANAGER_CONTEXT, context => {
            this.observe(context?.contents, contents => { this.contents = contents; this.updateResource(); }, "form-content");
            this.observe(context?.layouts, layouts => { this.layouts = layouts; this.updateResource(); }, "form-layout");
        });
        this.consumeContext(UMB_BLOCK_ELEMENT_PROPERTY_DATASET_CONTEXT, async context => {
            this.datasetContext = context;
            this.observe(await context?.propertyValueByAlias("formInterface"), value => {
                this.localResource = listItems(value)[0]?.resource;
                this.updateResource();
            }, "local-interface");
            this.observe(context?.properties, () => this.updateResource(), "field-context");
            this.updateResource();
        });
        this.consumeContext(UMB_BLOCK_ENTRIES_CONTEXT, context => {
            this.entriesContext = context;
            this.updateResource();
        });
    }
    updateResource() {
        this.form = enclosingForm(this.contents, this.layouts, this.datasetContext?.getUnique(),
            this.entriesContext?.getParentUnique?.());
    }
    availableFields(fields) { return fields.filter(field => !this.localResource || !field.readOnly); }
    render() {
        const picked = this.localResource ?? this.form?.resource;
        const resource = picked && this.catalogue?.formClasses?.find(r => r.selection === picked || r.alias === picked);
        const fields = this.availableFields(resource?.fields ?? []);
        return html`${this.renderChoices(fields.map(field => ({
                value: field.key, label: `${field.label} (${field.kind}) — ${field.key}`,
            })), this.localResource ? "Link field" : "Field binding")}
            ${!resource ? html`<p>Select the form class in its Form interface first.</p>` : ""}`;
    }
}
customElements.define("app-field-picker", FieldPicker);
