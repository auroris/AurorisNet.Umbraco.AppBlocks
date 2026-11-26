import assert from "node:assert/strict";
import { test } from "node:test";
import { createRequire } from "node:module";
import { existsSync } from "node:fs";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { runtimeAsset } from "./runtime-assets.mjs";

const root = fileURLToPath(new URL("../../", import.meta.url));
const require = createRequire(resolve(process.env.INTERACTIVE_REPORTS_ROOT || resolve(root, "../AurorisNet.InteractiveReports"), "package.json"));
const { chromium } = require("playwright");

test("a form updates visibility, required and read-only state and compares two dates before saving", async () => {
    const browser = await chromium.launch({ headless: true, ...(existsSync(chromium.executablePath()) ? {} : { executablePath: process.env.BROWSER_PATH || "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe" }) });
    try {
        const page = await browser.newPage();
        const errors = [], submissions = [];
        page.on("pageerror", error => errors.push(error.message));
        const record = { key: "1", values: { status: "Draft", reason: "", start: "2026-05-03", end: "2026-05-04", review: "Staff note" }, capabilities: { update: true }, readOnlyFields: ["review"] };
        const condition = (field, comparison, literal = "", otherField = "", kind = "text") => ({ field, comparison, literal, otherField, kind });
        const rule = (field, action, conditions, message = "") => ({ field, action, conditions, message, match: "All" });
        const cancelled = condition("status", "Is", "Cancelled");
        const rules = [rule("reason", "Show", [cancelled]), rule("reason", "Require", [cancelled]), rule("review", "Require", [cancelled]),
            rule("start", "Make read only", [cancelled]), rule("end", "Validate", [condition("end", "Is at least", "", "start", "date")], "Check the end date.")];
        const input = (name, type = "text") => '<div class="ah-grid__item"><div class="app-form-field"><label for="' + name + '">' + name
            + '<span data-required-mark hidden>*</span></label><input data-field="' + name + '" id="' + name + '" type="' + type + '"></div></div>';
        await page.route("**/*", async route => {
            const path = new URL(route.request().url()).pathname;
            const asset = await runtimeAsset(path);
            if (asset) return route.fulfill(asset);
            if (path.startsWith("/api/forms/")) {
                if (route.request().method() !== "GET") {
                    submissions.push(route.request().postDataJSON().values);
                    record.readOnlyFields = ["end"];
                }
                return route.fulfill({ json: { record, antiforgeryToken: "token", definitionVersion: "1" } });
            }
            return route.fulfill({ contentType: "text/html", body: '<form data-app-form data-api="/api/forms/test" data-key-parameter="id" data-field-rules=\''
                + JSON.stringify(rules) + '\' novalidate><div class="app-form-message"></div><div class="app-form-errors"></div><fieldset class="app-form-body" disabled>'
                + input("status") + input("reason") + input("start", "date") + input("end", "date") + input("review")
                + '<button data-save>Save</button></fieldset></form><script type="module" src="/App_Plugins/ApplicationBlocks/runtime.js"></script>' });
        });
        await page.goto("http://site.test/form?id=1");
        await page.waitForFunction(() => document.querySelector("#status").value === "Draft");
        assert.equal(await page.locator("#reason").isVisible(), false);
        await page.locator("#status").fill("Cancelled");
        assert.equal(await page.locator("#start").isDisabled(), true);
        assert.equal(await page.locator("#reason").isVisible(), true);
        assert.equal(await page.locator("#review").inputValue(), "Staff note");
        assert.equal(await page.locator("#review").isDisabled(), true);
        await page.locator("[data-save]").click();
        assert.equal(await page.locator("#reason").evaluate(el => el.validity.valueMissing), true);
        await page.locator("#reason").fill("Weather");
        await page.locator("#end").fill("2026-05-02");
        assert.equal(await page.locator("#end").evaluate(el => el.validationMessage), "Check the end date.");
        await page.locator("[data-save]").click();
        assert.equal(submissions.length, 0);
        await page.locator("#end").fill("2026-05-05");
        const saved = page.waitForResponse(r => r.request().method() === "PUT");
        await page.locator("[data-save]").click();
        await saved;
        await page.waitForFunction(() => document.querySelector("#end").disabled && !document.querySelector("#review").disabled);
        assert.deepEqual(submissions, [{ status: "Cancelled", reason: "Weather", end: "2026-05-05" }]);
        assert.deepEqual(errors, []);
    } finally { await browser.close(); }
});
