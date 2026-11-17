# Shuttle field

The **Shuttle** field (`appShuttle`) stores an ordered selection as one line of CSV. It shows the available items in a list on the left and the selected items in a list on the right.

A visitor adds or removes highlighted items, moves all items at once, and reorders the selected list with **Top**, **Up**, **Down** and **Bottom**. Several highlighted items keep their relative order. The lists are native multi-select controls, the buttons work with the keyboard, and the lists stack on narrow screens.

## Adding one

Add a Shuttle inside a Form in a Forms container. It can sit inside a fieldset or a layout. Bind it to a `text` field of the form class. Supply the items through that field's `Choices` or through a published **List of values**, as with Select:

```csharp
new FormField("steps", "Steps", "text", MaxLength: 2000,
    Choices: [new("review", "Review"), new("approve", "Approve"), new("publish", "Publish")])
```

## The stored value

The form class receives the whole right-hand list as one CSV string of item values, in the order shown. Publish above Review submits `publish,review`. An empty list submits `""`. Highlighting an item only selects it for the next move, so it has no effect on what is saved.

- A value that contains a comma or a double quote is CSV-escaped. The two values `north,east` and `say "yes"` become `"north,east","say ""yes"""`.
- Item values must be unique, non-empty and free of line breaks, so the output stays on one line. Labels can differ from values.
- The field's maximum length applies to the whole CSV string.

`ShuttleValue.TryParse(csv, out var items)` and `ShuttleValue.Serialize(items)`, in `AppBlocks.Forms`, read and write ordered selections in a class.

The server rejects malformed CSV, duplicate values and values that are not among the choices before it calls the class. A published list applies the same access checks as it does for Select.

A value saved on a record stays visible after it is retired from the list. It can be reordered or removed on that record, and it cannot be added to another.

## Settings

- **Default value** accepts ordered CSV or a bind variable such as `:APP.steps` or `:PARAM.steps`. It applies when a new record has no value.
- Required, read-only, hidden, help text and width work as for other fields. A required shuttle needs at least one item on the right.
- A read-only field, and a record the caller cannot create or update, disables both lists and every button.
