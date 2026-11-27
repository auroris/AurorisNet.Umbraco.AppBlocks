import { AppEditor } from "./editor-base.js";
export default class ListPicker extends AppEditor {
    render() {
        const lists = this.catalogue?.lists ?? [];
        const selected = lists.find(list => list.selection === this.value || list.alias === this.value);
        return this.renderChoices(lists.map(list => ({ value: list.key, label: `${list.label} (${list.alias})` })),
            "List of values", selected?.key ?? this.value);
    }
}
customElements.define("app-list-picker", ListPicker);
