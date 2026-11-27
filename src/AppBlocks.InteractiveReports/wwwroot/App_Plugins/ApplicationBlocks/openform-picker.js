import ParentFormPicker from "./parent-form-picker.js";

export default class OpenFormPicker extends ParentFormPicker {
    get pageProperty() { return "formPage"; }
    get label() { return "Open form"; }
}
customElements.define("app-open-form-picker", OpenFormPicker);
