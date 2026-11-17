# Color picker

The **Color picker** field (`appColor`) stores a six-digit hex color. Add it inside a Form in a Forms container and bind it to a `text` field that holds at least seven characters:

```csharp
new FormField("color", "Color", "text", MaxLength: 7)
```

## Values

A visitor uses the browser's color picker or types a six-digit hex color, such as `#2a5d8f`. Uppercase and lowercase hex digits are accepted. The browser and the server both check the format, and both refuse named colors, three-digit hex and alpha values.

**Clear** removes the selection. An empty optional field submits `null`. A required field needs a color before saving.

A checkerboard swatch stands for an empty or invalid value. An invalid value already stored on a record stays visible in the text field, so it can be corrected.

## Settings

The block has the label, help, required, read-only, hidden, width, placeholder and default value settings of other fields.

- A default fills an empty field on a new record only. It is a literal hex color or a bind variable such as `:PARAM.color`.
- A read-only field, and a form the caller cannot write, disables the picker, the text field and the Clear button.
