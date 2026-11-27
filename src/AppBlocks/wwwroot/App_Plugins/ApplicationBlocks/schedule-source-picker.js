import { AppEditor } from "./editor-base.js";

export default class ScheduleSourcePicker extends AppEditor {
    render() {
        const options = [
            ...(this.catalogue?.scheduleReports ?? []).map(report => ({ value: report.selection, label: report.label, alias: report.alias, group: "Reports" })),
            ...(this.catalogue?.resources ?? []).filter(r => r.dataSource).map(r => ({ value: r.selection, label: r.dataSource.name, alias: r.alias, group: "Data sources" })),
        ];
        return this.renderChoices(options, "Schedule source", options.find(o => o.alias === this.value)?.value ?? this.value);
    }
}
customElements.define("app-schedule-source-picker", ScheduleSourcePicker);
