import assert from "node:assert/strict";
import { test } from "node:test";
import { createRequire } from "node:module";
import { existsSync } from "node:fs";
import { readFile } from "node:fs/promises";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { runtimeAsset } from "./runtime-assets.mjs";

const root = fileURLToPath(new URL("../../", import.meta.url));
const require = createRequire(resolve(process.env.INTERACTIVE_REPORTS_ROOT || resolve(root, "../AurorisNet.InteractiveReports"), "package.json"));
const { chromium, expect } = require("@playwright/test");

test("a key-only picker in a side panel resolves a saved name, replaces it and clears it", async t => {
    const browser = await chromium.launch({ headless: true, ...(existsSync(chromium.executablePath()) ? {} :
        { executablePath: process.env.BROWSER_PATH || "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe" }) });
    t.after(() => browser.close());
    const context = await browser.newContext();
    const page = await context.newPage();
    const members = [{ key: "12", displayName: "Taylor", detail: "Member · taylor" },
        { key: "backoffice:123", displayName: "Taylor", detail: "Backoffice User · editor" }];
    const form = "/site/custom/forms/page/form", picker = "/site/custom/account-picker/page/picker";
    const prefix = "/site/App_Plugins/ApplicationBlocks/";
    const popupHtml = await readFile(resolve(root, "src/AppBlocks/wwwroot/App_Plugins/ApplicationBlocks/account-picker.html"), "utf8");
    let saved = members[0].key;
    await context.route("**/*", async route => {
        const url = new URL(route.request().url());
        if (url.pathname === prefix + "account-picker.html") return route.fulfill({ contentType: "text/html", body: popupHtml });
        const asset = await runtimeAsset(url.pathname);
        if (asset) return route.fulfill(asset);
        if (url.pathname === picker) return route.fulfill({ json: url.searchParams.has("key")
            ? { person: members.find(item => item.key === url.searchParams.get("key")) } : { items: members } });
        if (url.pathname.startsWith(form)) {
            if (route.request().method() === "PUT") saved = route.request().postDataJSON().values.account;
            return route.fulfill({ json: { definitionVersion: "1", antiforgeryToken: "token",
                record: { key: "1", checksum: "1", values: { account: saved }, capabilities: { update: true } } } });
        }
        if (url.pathname !== "/site/form") return route.abort();
        return route.fulfill({ contentType: "text/html", body: `<!doctype html><html lang="fr"><body>
            <script type="application/json" data-appblocks-strings>{"pickerFind":"Rechercher un compte"}</script>
            <button type="button" data-open-panel="panel">Edit</button><dialog id="panel" data-app-form-panel>
            <header class="app-panel-header"><button data-panel-close>Close</button></header>
            <form data-app-form data-api="${form}" data-key-parameter="id"><div class="app-form-message"></div><div class="app-form-errors" hidden></div>
            <fieldset class="app-form-body" disabled><input type="hidden" data-field="account" data-kind="text">
            <div data-app-account-picker data-endpoint="${picker}" data-settings='{"identityField":"account","allowsEmpty":true}'>
            <input data-current-name readonly aria-label="Selected account"><button type="button" data-find>Find account</button>
            <button type="button" data-manual hidden>Clear selection</button><span data-status></span></div>
            <button type="submit" data-save>Save</button></fieldset></form></dialog>
            <script type="module" src="${prefix}runtime.js"></script></body></html>` });
    });
    await page.goto("http://picker.test/site/form?id=1");
    await page.getByText("Edit", { exact: true }).click();
    const name = page.getByLabel("Selected account");
    await expect(name).toHaveValue("Taylor");
    const opening = page.waitForEvent("popup");
    await page.getByText("Find account").click();
    const popup = await opening;
    await expect(popup).toHaveTitle("Rechercher un compte");
    await popup.getByRole("searchbox").fill("Taylor");
    await popup.getByRole("button", { name: "Taylor Backoffice User · editor" }).click();
    await expect(page.locator("[data-field=account]")).toHaveValue(members[1].key);
    await page.getByText("Save", { exact: true }).click();
    await expect(page.locator("dialog")).toBeHidden();
    assert.equal(saved, members[1].key);
    await page.getByText("Edit", { exact: true }).click();
    await expect(name).toHaveValue("Taylor");
    await page.getByText("Clear selection").click();
    await expect(name).toHaveValue("");
    await page.getByText("Save", { exact: true }).click();
    await expect(page.locator("dialog")).toBeHidden();
    assert.equal(saved, null);
});

// A page names a key with app-form:select-account on the picker's key field; the runtime mounts the form and the picker.
async function selectionPage(t, { update = true, readOnlyFields = [], lookup } = {}) {
    const browser = await chromium.launch({ headless: true, ...(existsSync(chromium.executablePath()) ? {} :
        { executablePath: process.env.BROWSER_PATH || "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe" }) });
    t.after(() => browser.close());
    const page = await browser.newPage(), errors = [], lookups = [];
    page.on("pageerror", error => errors.push(error.message));
    t.after(() => assert.deepEqual(errors, []));
    const people = {
        "k-1": { key: "k-1", displayName: "Avery", attributes: { mail: "avery@example.invalid", employeeNumber: "a 111" } },
        "k-2": { key: "k-2", displayName: "Blake", attributes: { mail: "blake@example.invalid", employeeNumber: "b 222" } },
    };
    const form = "/site/custom/forms/page/form", picker = "/site/custom/account-picker/page/picker";
    const settings = JSON.stringify({ identityField: "account", nameField: "name", fieldMappings: { employeeNumber: "number" },
        prefillMappings: { mail: "email" }, fallbackFields: { number: "mirror" }, compactUpperFields: ["number"], allowManualEntry: true });
    await page.route("**/*", async route => {
        const url = new URL(route.request().url());
        const asset = await runtimeAsset(url.pathname);
        if (asset) return route.fulfill(asset);
        if (url.pathname === picker) {
            const key = url.searchParams.get("key");
            lookups.push(key);
            const answer = lookup ? lookup(key) : { person: people[key] };
            return route.fulfill({ status: answer.status ?? 200, json: answer });
        }
        if (url.pathname.startsWith(form)) return route.fulfill({ json: { definitionVersion: "1", antiforgeryToken: "token",
            record: { key: "1", checksum: "1", values: { email: readOnlyFields.length ? "filed@example.invalid" : null }, readOnlyFields, capabilities: { update } } } });
        if (url.pathname !== "/site/form") return route.abort();
        return route.fulfill({ contentType: "text/html", body: `<!doctype html><html lang="en"><body>
            <form data-app-form data-api="${form}" data-key-parameter="id"><div class="app-form-message"></div><div class="app-form-errors" hidden></div>
            <fieldset class="app-form-body" disabled>
            <label>Name <input data-field="name"></label><input type="hidden" data-field="account">
            <div data-app-account-picker data-endpoint="${picker}" data-settings='${settings}'>
            <button type="button" data-find>Find account</button><button type="button" data-manual hidden>Enter manually</button><span data-status></span></div>
            <label>Email <input data-field="email"></label><label>Number <input data-field="number"></label><input type="hidden" data-field="mirror">
            <button type="submit" data-save>Save</button></fieldset></form>
            <script type="module" src="/site/App_Plugins/ApplicationBlocks/runtime.js"></script></body></html>` });
    });
    await page.goto("http://picker.test/site/form?id=1");
    await expect(page.locator(".app-form-body")).toBeEnabled();
    const select = key => page.evaluate(key => !document.querySelector('[data-field="account"]').dispatchEvent(
        new CustomEvent("app-form:select-account", { cancelable: true, detail: { key } })), key);
    const values = () => page.evaluate(() => ["account", "name", "email", "number", "mirror"].map(key =>
        document.querySelector(`[data-field="${key}"]`).value));
    return { page, select, values, lookups, status: page.locator("[data-status]") };
}

test("app-form:select-account selects and locks like a choice in the search window, and replaces an earlier one", async t => {
    const { page, select, values, lookups, status } = await selectionPage(t);
    assert.equal(await select("k-1"), true);
    await expect(status).toHaveText("Avery selected.");
    assert.deepEqual(await values(), ["k-1", "Avery", "avery@example.invalid", "A111", "A111"]);
    await expect(page.getByLabel("Name")).not.toBeEditable();
    await expect(page.getByLabel("Number")).not.toBeEditable();
    await expect(page.getByText("Enter manually")).toBeVisible();
    assert.equal(await select("k-2"), true);
    await expect(status).toHaveText("Blake selected.");
    assert.deepEqual(await values(), ["k-2", "Blake", "blake@example.invalid", "B222", "B222"]);
    // Only the key travels; the lookup supplies everything else.
    assert.deepEqual(lookups, ["k-1", "k-2"]);
    await page.getByText("Enter manually").click();
    await expect(page.getByLabel("Name")).toBeEditable();
    assert.equal((await values())[0], "");
});

test("app-form:select-account does nothing on a read-only form and fills a disabled mapped field as a search-window choice does", async t => {
    const viewOnly = await selectionPage(t, { update: false });
    assert.equal(await viewOnly.select("k-1"), true, "the picker still owns its field");
    await viewOnly.page.waitForTimeout(100);
    assert.deepEqual(await viewOnly.values(), ["", "", "", "", ""]);
    assert.deepEqual(viewOnly.lookups, []);

    // A page may disable a mapped field only for now, such as a collapsed section, so the picker fills it either way.
    const filed = await selectionPage(t, { readOnlyFields: ["email"] });
    await filed.select("k-1");
    await expect(filed.status).toHaveText("Avery selected.");
    assert.deepEqual(await filed.values(), ["k-1", "Avery", "avery@example.invalid", "A111", "A111"]);
});

test("a refused app-form:select-account lookup changes nothing and says so", async t => {
    const { page, select, values, status } = await selectionPage(t, { lookup: key => key === "k-1"
        ? { person: { key: "k-1", displayName: "Avery", attributes: { mail: "avery@example.invalid", employeeNumber: "a 111" } } }
        : { status: 422, title: "Not selectable" } });
    await select("k-1");
    await expect(status).toHaveText("Avery selected.");
    await select("k-2");
    await expect(status).toHaveText("This selection could not be validated. Try again.");
    await expect(page.locator("[data-status].app-picker-error")).toBeVisible();
    assert.deepEqual(await values(), ["k-1", "Avery", "avery@example.invalid", "A111", "A111"]);
});
