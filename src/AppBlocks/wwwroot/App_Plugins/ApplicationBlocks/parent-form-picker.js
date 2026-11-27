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
    static properties = { ...AppEditor.properties, local: { state: true }, documentForms: { state: true }, useThisPage: { state: true }, pickedPage: { state: true }, remote: { state: true } };
    get pageProperty() { return "parentPage"; }
    get label() { return "Parent form"; }
    get createDestination() { return false; }
    constructor() {
        super();
        this.documents = new UmbDocumentDetailRepository(this);
        this.consumeContext(UMB_BLOCK_MANAGER_CONTEXT, manager =>
            this.observe(manager?.contents, items => this.local = items, "parent-grid"));
        this.consumeContext(UMB_BLOCK_ELEMENT_PROPERTY_DATASET_CONTEXT, async dataset => {
            this.datasetContext = dataset;
            const update = () => this.loadPage(this.createPage || (this.useThisPage ? null : this.openPage));
            this.observe(await dataset?.propertyValueByAlias(this.pageProperty), page => { this.openPage = page; update(); }, "parent-page");
            if (this.pageProperty === "formPage") {
                this.observe(await dataset?.propertyValueByAlias("formOnThisPage"), enabled => { this.useThisPage = [true, 1, "1", "true"].includes(enabled); update(); }, "this-page");
                if (this.createDestination)
                    this.observe(await dataset?.propertyValueByAlias("createFormPage"), page => { this.createPage = page; update(); }, "create-page");
            }
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
        if (this.pageProperty === "formPage" && !this.useThisPage && !this.pickedPage) return this.renderChoices([], this.label);
        const options = new Map();
        const forms = this.pickedPage ? this.remote ?? [] : [...(this.documentForms ?? []), ...(this.local ?? [])];
        for (const form of forms) {
            if (form.key === this.datasetContext?.getUnique()) continue;
            const content = values(form);
            const [config] = listItems(content.formInterface);
            if (!config) continue;
            const parameter = config.keyParameter || (content.parentForm ? "detailId" : "id");
            const resource = this.catalogue?.formClasses.find(r => r.selection === config.resource)?.alias ?? config.resource;
            options.set(form.key, { value: form.key, label: (content.heading || resource || "Form") + " (" + parameter + ")" });
        }
        return this.renderChoices([...options.values()], this.label);
    }
}
customElements.define("app-parent-form-picker", ParentFormPicker);
