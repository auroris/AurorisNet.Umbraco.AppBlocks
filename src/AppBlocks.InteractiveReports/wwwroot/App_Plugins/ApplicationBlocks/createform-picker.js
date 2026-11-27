import ParentFormPicker from "./parent-form-picker.js";

export default class CreateFormPicker extends ParentFormPicker {
    get pageProperty() { return "formPage"; }
    get createDestination() { return true; }
    get label() { return "Create form"; }
}
customElements.define("app-create-form-picker", CreateFormPicker);
