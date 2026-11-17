# Display value

The **Display value** field (`appDisplay`) shows a field of the form's record that the form reads and never writes: a reference number, a status the server decides, the person who filed a request. It can show the value as a link.

Bind it to any field of the form class or data source. It renders as a read-only control and has the label, help, hidden, default value and width settings of other fields. Its value stays out of every save.

## Showing the value as a link

**Link template** turns the value into a link. The value is the link's text and the template is its address. A field alias in braces becomes that field's value:

| Link template | Leads to |
| --- | --- |
| `/customers/?id={customerId}` | The customer's page, as a path from the site's root |
| `../order/?id={orderId}` | The order's page, relative to the page the form is on |
| `mailto:{email}?subject=Request%20{reference}` | A new message in the reader's mail program, addressed and titled |
| `tel:{phone}` | A call from the reader's phone or softphone |
| `{website}` | The address the field holds |

For example, a request form shows who filed the request as a link that starts an email about it. It needs two blocks: a Display value bound to `requesterEmail` with the `mailto:` template above, and a hidden Text field bound to `reference`.

The page builds the address in the browser from the record the form loaded, when the record loads and again after each save.

- **Every field a template names needs a field block on the form**, because the loaded record holds the form's own fields. Add a hidden field for a value the address needs and the page does not show. Publishing names any field the form lacks.
- **Each value is percent-encoded** as it goes into the address, so a customer number of `7&admin=1` becomes `7%26admin%3D1`. In a `mailto:` or `tel:` address, `@` and `+` are kept as written.
- **Write the template's own text encoded.** A space in a subject is `%20`.
- **A template that is one field alone**, such as `{website}`, takes the whole address from that field's value.
- **An empty value shows no link.** A new record's Display value is empty until the record is saved, unless the block has a default.

### Which addresses a link may have

A link is a path from the site's root, a path relative to the page, or an `http:`, `https:`, `mailto:` or `tel:` address. A path from the site's root gets the site's path base, so `/customers/` works on a site served under `/intranet`.

Publishing checks the template against that rule. It refuses any other scheme, such as `javascript:`, and an address that starts with two slashes. The page applies the same rule to the address it builds, which covers a template that takes its address from a field: a value the rule refuses is shown as plain text.

### Mail links

A `mailto:` link opens a message in the reader's own mail program, with the address, subject and body the template gives it. The reader sends the message, and the site keeps no record of it. Whatever the template puts in the subject and body leaves the site in that email, so name the record by its reference and keep its details on the page.

## Styling

The link has the class `app-display-link`, the accent colour of the runtime stylesheet and the height of a control, so it lines up with the fields beside it. [Styling](site-integration.md#styling) lists the tokens.
