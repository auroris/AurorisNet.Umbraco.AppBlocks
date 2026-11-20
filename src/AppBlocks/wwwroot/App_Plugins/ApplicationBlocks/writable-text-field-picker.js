import FieldPicker from "./field-picker.js";
import { writableTextBindings } from "./form-context.js";

export default class WritableTextFieldPicker extends FieldPicker {
    availableFields(fields) {
        const bindings = writableTextBindings(this.form);
        return fields.filter(field => field.kind === "text" && !field.readOnly && bindings.has(field.key));
    }
}
customElements.define("app-writable-text-field-picker", WritableTextFieldPicker);
