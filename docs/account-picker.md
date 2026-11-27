# Umbraco Account Picker

Add an **Umbraco Account Picker** inside a form. A signed-in website account with access to the page can search by display name or username. Each result shows its account type and username, including when two accounts have the same display name.

1. Add a writable **Text** block for the account key. You can hide this field. Bind it to a text field large enough for a backoffice key (47 characters) if the picker offers backoffice Users.
2. Optionally add a second writable **Text** block for the display name. The picker fills this from Umbraco. With only a key binding, it looks up the name again when the form opens.
3. Add the picker, choose the fields, and select **Members**, **Backoffice Users**, or **Both**. The default is **Members**. To require a selection, mark the key field required.

Member keys use the decimal Member ID. Backoffice keys use `backoffice:` followed by the User GUID. These are the same keys AppBlocks uses for the signed-in account. Choosing an account supplies a form value. The form resource or data-source policy decides what that value means for access.

AppBlocks checks each new or changed selection against the published picker and the current account when the form is saved. Locked or unapproved accounts are ineligible. An unchanged link keeps its stored key and name on unrelated edits. Clearing an optional selection clears both fields. Each picker owns its bindings within the form.

The picker follows the [Visibility, Security and Read Only](business-rules.md#block-visibility-and-security) conditions of its form. A locked picker locks every field it writes. When Security omits the picker or one of those fields for a visitor, the other fields it writes are read only for that visitor.

## Selecting an account from a page script

A page script can select an account by its key, for example to offer a "Use my details" button. Dispatch `app-form:select-account` on the key field the picker fills, with the key as the only detail:

```js
const handled = !keyField.dispatchEvent(new CustomEvent("app-form:select-account", { cancelable: true, detail: { key } }));
```

The picker looks the key up through its own endpoint, as for a choice in its search window, so the same checks decide who may be selected. Then it fills the name and every mapped field, locks them, and shows its status line. A lookup that is refused or fails changes no field and shows the picker's usual message. A selection replaces any earlier one. While the picker could not be used by hand (a read-only or view-only form, or a key or name field that is read only), the event does nothing.

The picker cancels the event whenever it owns that field, so `handled` tells a field with a picker from one without. The event carries no name or attribute values, and the picker ignores anything other than `key`. Mapped fields are filled exactly as for a choice in the search window, including one that is currently disabled.

On an existing site, add the picker to the **Form fields** block group if the site's grid does not already offer it. The package supplies the updated grid on a fresh install or when the package definitions are reinstalled.

## Adding another account source

Derive a block from `AccountPickerBlock` and implement `ReadSettings` for its published configuration. Register the block as an `IAppBlockPlugin` and register its scoped `IAccountPickerProvider` under the same alias.

```csharp
services.AddSingleton<IAppBlockPlugin, StaffAccountPickerBlock>();
services.AddKeyedScoped<IAccountPickerProvider, StaffAccountProvider>(StaffAccountPickerBlock.AliasName);
```

The provider implements `SearchAsync` and `ResolveAsync`. It returns a provider-specific key and display name, with an optional detail line and mapped attributes. Both operations receive the published block settings. Only save-time resolution receives a `FormExecutionContext`. AppBlocks supplies the popup, page admission, field binding and submission processing. AppHost’s AD People Picker uses this interface with Windows SIDs and directory-specific search settings.
