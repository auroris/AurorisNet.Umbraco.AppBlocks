import { AppEditor } from "./editor-base.js";
export default class ListPicker extends AppEditor {
    render() {
        const lists = this.catalogue?.lists ?? [];
        const valueOf = list => list.selection ?? list.key;
        const selected = lists.find(list => [list.selection, list.key, list.alias].includes(this.value));
        return this.renderChoices(lists.map(list => ({ value: valueOf(list), label: `${list.label} (${list.alias})` })),
            "List of values", selected ? valueOf(selected) : this.value);
    }
}
customElements.define("app-list-picker", ListPicker);
