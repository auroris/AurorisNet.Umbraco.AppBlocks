import { mountForm } from "./forms.js";
import { mountAttachments } from "./attachments.js";
import { mountTabular } from "./tabular.js";
import { mountSchedule } from "./schedule.js";
import { mountFormPanel } from "./form-panel.js";

// Each panel keeps its unmounted form until opened. Its controls get a fresh record and submission key on every visit.
function mountChildren(root) {
    const dispose = [...root.querySelectorAll("[data-app-attachments]")].map(mountAttachments);
    root.querySelectorAll("[data-app-tabular]").forEach(mountTabular);
    dispose.push(...[...root.querySelectorAll("[data-app-schedule]")].map(mountSchedule));
    return () => dispose.forEach(cleanup => cleanup?.());
}

document.querySelectorAll("dialog[data-app-form-panel]").forEach(dialog => mountFormPanel(dialog, mountChildren));
mountChildren(document);
document.querySelectorAll("[data-app-form]").forEach(form => mountForm(form));
