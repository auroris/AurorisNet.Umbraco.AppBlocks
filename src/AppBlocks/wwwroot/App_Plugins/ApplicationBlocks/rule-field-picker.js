import { AppEditor } from "./editor-base.js";
import { UMB_BLOCK_WORKSPACE_CONTEXT } from "@umbraco-cms/backoffice/block";
import { enclosingWorkspace } from "./workspace-context.js";
import { appBlockTypes } from "./block-types.js";
import { listItems, values } from "./preview-model.js";

// The rule and its conditions have their own block workspaces. Subscribe to the surrounding
// Form's working copy so changing its Form interface updates both pickers before Submit.
const formType = appBlockTypes.find(type => type.alias === "appForm").key;

export default class RuleFieldPicker extends AppEditor {
    static properties = { ...AppEditor.properties, resource: { state: true } };
    async connectedCallback() {
        super.connectedCallback();
        const form = await enclosingWorkspace(this, UMB_BLOCK_WORKSPACE_CONTEXT, w => w.content.getContentTypeId() === formType);
        if (this.isConnected) this.observe(form?.content.data, data => {
            this.resource = listItems(values(data).formInterface)[0]?.resource;
        }, "rule-form");
    }
    render() {
        const resource = this.catalogue?.formClasses?.find(item => item.alias === this.resource);
        return this.renderChoices((resource?.fields ?? []).map(f => ({ value: f.key, label: f.label })), "Field");
    }
}
customElements.define("app-rule-field-picker", RuleFieldPicker);
