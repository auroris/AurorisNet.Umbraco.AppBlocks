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
