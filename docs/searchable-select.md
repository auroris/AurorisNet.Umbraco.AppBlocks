# Searchable select

Add **Searchable select** inside an AppBlocks form. Visitors can type part of a label to filter the choices, use the arrow keys and Enter to select a match, and remove a selection to change it.

## Use a table column

1. Under **Data sources**, create a **List of values** and give it an alias such as `departments`.
2. Choose the connection and enter a query such as `SELECT DISTINCT Department FROM Offices WHERE Department IS NOT NULL`.
3. Set **Display column** and **Value column** to `Department` and publish the list.
4. On the field block, choose the destination **Field binding** and select **departments** under **List of values**.

The form searches the choices loaded for the visitor. The list's data-source access rules and **Maximum rows** setting apply. The default limit is 1,000 rows. For a column with repeated values, `SELECT DISTINCT` removes duplicates before the row limit. A list can also display a label and store a separate key. A form class that supplies its own field choices can leave **List of values** empty.

By default, the dropdown shows up to **50** matching choices at a time while still searching every loaded choice as you type. Set **Maximum visible choices** on the field block to change how many matching choices appear in the dropdown, or enter `0` to show all loaded choices.

## Store one or more values

By default the form stores one value exactly as selected, including any commas. A single selection from a published list can bind to a text or number field.

Turn on **Allow multiple values** and bind to a text field to save the selection in order. The field uses the same one-line CSV format as [Shuttle](shuttle.md). Selecting `North, East` and `West` stores `"North, East",West`. Clearing all selections stores an empty string. The field's maximum length applies to the whole stored value.

The server checks submitted selections against the available choices. When a published list retires a value, an existing record can keep or remove it. Defaults use a single value or CSV according to the field's setting.
