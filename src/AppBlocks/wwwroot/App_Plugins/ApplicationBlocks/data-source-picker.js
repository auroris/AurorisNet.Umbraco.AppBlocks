import { AppEditor } from "./editor-base.js";
export default class DataSourcePicker extends AppEditor {
    render() {
        const sources = (this.catalogue?.resources ?? []).filter(source => source.dataSource);
        const valueOf = source => source.selection ?? source.dataSource.key;
        const selected = sources.find(source => [source.selection, source.dataSource.key, source.alias].includes(this.value));
        return this.renderChoices(sources.map(source => ({
            value: valueOf(source),
            label: `${source.dataSource.name} (${source.alias})`
        })), "SQL data source", selected ? valueOf(selected) : this.value);
    }
}
customElements.define("app-data-source-picker", DataSourcePicker);
