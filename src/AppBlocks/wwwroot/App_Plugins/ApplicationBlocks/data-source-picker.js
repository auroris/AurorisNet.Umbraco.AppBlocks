import { AppEditor } from "./editor-base.js";
export default class DataSourcePicker extends AppEditor {
    render() {
        const sources = (this.catalogue?.resources ?? []).filter(source => source.dataSource);
        const selectedKey = sources.find(source => source.alias === this.value || source.selection === this.value)?.dataSource.key ?? this.value;
        return this.renderChoices(sources.map(({ alias, dataSource }) => ({
            value: dataSource.key,
            label: `${dataSource.name} (${alias})`
        })), "SQL data source", selectedKey);
    }
}
customElements.define("app-data-source-picker", DataSourcePicker);
