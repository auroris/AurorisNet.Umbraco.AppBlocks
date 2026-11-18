import { AppEditor } from "./editor-base.js";
import { UMB_DOCUMENT_WORKSPACE_CONTEXT, UmbDocumentDetailRepository } from "@umbraco-cms/backoffice/document";
import { UMB_BLOCK_MANAGER_CONTEXT, UMB_BLOCK_ELEMENT_PROPERTY_DATASET_CONTEXT } from "@umbraco-cms/backoffice/block";
import { enclosingWorkspace } from "./workspace-context.js";
import { json, listItems, values } from "./preview-model.js";

function formsIn(properties) {
    const forms = [];
    for (const property of properties ?? []) {
        const block = json(property.value);
        for (const content of block?.contentData ?? []) {
            if (values(content).formInterface) forms.push(content);
            forms.push(...formsIn(content.values));
        }
    }
    return forms;
}
export default class ParentFormPicker extends AppEditor {
    static properties = { ...AppEditor.properties, local: { state: true }, documentForms: { state: true }, pickedPage: { state: true }, remote: { state: true } };
    constructor() {
        super();
        this.documents = new UmbDocumentDetailRepository(this);
        this.consumeContext(UMB_BLOCK_MANAGER_CONTEXT, manager =>
            this.observe(manager?.contents, items => this.local = items, "parent-grid"));
        this.consumeContext(UMB_BLOCK_ELEMENT_PROPERTY_DATASET_CONTEXT, async dataset => {
            this.datasetContext = dataset;
            this.observe(await dataset?.propertyValueByAlias("parentPage"), page => this.loadPage(page), "parent-page");
        });
    }
    async connectedCallback() {
        super.connectedCallback();
        const workspace = await enclosingWorkspace(this, UMB_DOCUMENT_WORKSPACE_CONTEXT);
        if (this.isConnected) this.observe(workspace?.data, data => this.documentForms = formsIn(data?.values), "parent-document");
    }
    async loadPage(page) {
        if (this.pickedPage === page) return;
        this.pickedPage = page;
        this.remote = [];
        if (!page) return;
        const { data } = await this.documents.requestByUnique(page);
        if (this.pickedPage === page) this.remote = formsIn(data?.values);
    }
    render() {
        const options = new Map();
        const forms = this.pickedPage ? this.remote ?? [] : [...(this.documentForms ?? []), ...(this.local ?? [])];
        for (const form of forms) {
            if (form.key === this.datasetContext?.getUnique()) continue;
            const content = values(form);
            const [config] = listItems(content.formInterface);
            if (!config) continue;
            const parameter = config.keyParameter || (content.parentForm ? "detailId" : "id");
            options.set(form.key, { value: form.key, label: (content.heading || config.resource || "Form") + " (" + parameter + ")" });
        }
        return this.renderChoices([...options.values()], "Parent form");
    }
}
customElements.define("app-parent-form-picker", ParentFormPicker);
