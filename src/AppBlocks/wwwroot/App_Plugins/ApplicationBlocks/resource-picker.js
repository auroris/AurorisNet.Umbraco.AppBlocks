import { AppEditor } from "./editor-base.js";
import { html } from "@umbraco-cms/backoffice/external/lit";
export default class ResourcePicker extends AppEditor {
    render() {
        const selected = this.catalogue?.resources.find(resource => [resource.selection, resource.alias].includes(this.value));
        return html`${this.renderChoices((this.catalogue?.resources ?? []).map(resource => ({
            value: resource.selection ?? resource.alias,
            label: resource.dataSource ? `${resource.dataSource.name} (${resource.alias}) — Data source` : `${resource.alias} — C# resource`,
        })), "Form resource", selected?.selection ?? this.value)}
        ${selected?.dataSource ? html`<p>${selected.dataSource.connection} · ${selected.fields.length} fields</p>` : ""}
        ${selected?.dataSource?.problem ? html`<p class="error" role="alert">${selected.dataSource.problem}</p>` : ""}`;
    }
}
customElements.define("app-resource-picker", ResourcePicker);
