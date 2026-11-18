import { runtimeAsset } from "./runtime-assets.mjs";
import assert from "node:assert/strict";
import { test, before, after } from "node:test";
import { createRequire } from "node:module";
import { readFile } from "node:fs/promises";
import { existsSync } from "node:fs";
import { resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const root = fileURLToPath(new URL("../../", import.meta.url));
const reportsRoot = process.env.INTERACTIVE_REPORTS_ROOT || resolve(root, "../AurorisNet.InteractiveReports");
const require = createRequire(resolve(reportsRoot, "package.json"));
const { chromium } = require("playwright");
const { reportState, hydratedResult } = await import(pathToFileURL(resolve(reportsRoot, "tests/ui/report-state-fixture.js")));
let browser;
before(async () => {
    const edge = "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe";
    browser = await chromium.launch({ headless: true, ...(existsSync(chromium.executablePath()) ? {} : { executablePath: process.env.BROWSER_PATH || edge }) });
});
after(async () => browser?.close());

const refusal = { status: 401, contentType: "application/problem+json", body: JSON.stringify({ code: "IR-1000", title: "Authentication required", detail: "Sign in to perform this operation." }) };

/** The markup InteractiveReport.cshtml renders and the real reports.js and widget, over a server that answers as told. */
async function mount({ anonymous = true, schema = null, loaded = null, setupNote = null, panels = "", html = "", prepare } = {}) {
    const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
    const asked = [], errors = [];
    page.on("pageerror", error => errors.push(error.message));
    await page.route("**/*", async route => {
        const url = new URL(route.request().url());
        const path = url.pathname;
        const json = value => route.fulfill({ contentType: "application/json", body: JSON.stringify(value) });
        if (path === "/sub/orders") return route.fulfill({ contentType: "text/html", body: `<!doctype html><html><body>
            <section class="app-block-report" data-form-panels="${panels}">
                <template data-app-report data-state-key="test" data-anonymous="${anonymous ? "true" : ""}" data-setup-note="${setupNote === null ? "" : "/sub/api/reports/__saved-views-setup"}">
                    <interactive-report report="orders" report-title="Orders" api-base="/sub/api/reports?appCulture=en" download-base="/sub/api/download?appCulture=en"></interactive-report>
                </template>
            </section>${typeof html === "function" ? html(url) : html}
            <script type="module" src="/sub/App_Plugins/ApplicationBlocks/reports.js"></script></body></html>` });
        if (path.includes("/App_Plugins/ApplicationBlocks/")) {
            const name = path.split("/").at(-1);
            return route.fulfill({ contentType: name.endsWith("css") ? "text/css" : "text/javascript", body: await readFile(resolve(root, "src/AppBlocks.InteractiveReports/wwwroot/App_Plugins/ApplicationBlocks", name), "utf8") });
        }
        asked.push(path + url.search);
        if (path.endsWith("/ui/ir.js")) return route.fulfill({ contentType: "text/javascript", body: await readFile(resolve(reportsRoot, "src/InteractiveReport.Client.Json/Ui/dist/ir.js"), "utf8") });
        if (path.endsWith("/__saved-views-setup")) return route.fulfill(setupNote ?? { status: 204 });
        if (path.endsWith("/whoami")) return json({ identity: "reader" });
        if (path.endsWith("/schema")) return schema ? route.fulfill(schema) : json({ title: "Orders", defaultState: reportState(), columns: [{ name: "ID", label: "ID", type: "number" }], features: ["download"], limits: { defaultPageSize: 25, maxPageSize: 100 }, capabilities: { aggregateFunctions: {}, expressionFunctions: [] } });
        if (path.endsWith("/default")) return loaded ? route.fulfill(loaded) : json({ summary: null, result: hydratedResult() });
        if (path.endsWith("/orders")) return json([]);
        return route.fulfill({ status: 404, contentType: "application/json", body: "{}" });
    });
    await prepare?.(page);
    await page.goto("http://app.test/sub/orders?customer=7");
    return { page, asked, errors };
}

test("a developer's policy refusing an anonymous visitor shows the block's notice, not the widget's error", async () => {
    for (const status of [401, 403]) {
        const { page, asked, errors } = await mount({ schema: { ...refusal, status } });
        await page.locator("section.app-block-report .app-block-signin[role=status]").getByText("Sign in to use this report.").waitFor();
        assert.equal(await page.locator("interactive-report").count(), 0);
        assert.equal(await page.locator("template[data-app-report]").count(), 0);
        assert.equal(await page.getByText("Authentication required").count(), 0);
        // It asked about the block as the widget would, with the page's address, and did not load the widget for nothing.
        assert.ok(asked.some(url => url.startsWith("/sub/api/reports/orders/schema?appCulture=en&appPageQuery=%3Fcustomer%3D7")), asked.join(" "));
        assert.ok(!asked.some(url => url.endsWith("/ui/ir.js")), asked.join(" "));
        assert.deepEqual(errors, []);
        await page.close();
    }
});

test("an anonymous visitor the report admits gets the report, and an error that is no refusal stays the widget's to show", async () => {
    const admitted = await mount();
    await admitted.page.locator("interactive-report").waitFor();
    await admitted.page.waitForFunction(() => document.querySelector("interactive-report").shadowRoot?.textContent?.includes("Orders") ?? document.querySelector("interactive-report").textContent.includes("Orders"));
    assert.equal(await admitted.page.locator(".app-block-signin").count(), 0);
    assert.deepEqual(admitted.errors, []);
    await admitted.page.close();

    const broken = await mount({ schema: { status: 500, contentType: "application/json", body: "{}" } });
    await broken.page.locator("interactive-report").waitFor();
    assert.equal(await broken.page.locator(".app-block-signin").count(), 0);
    await broken.page.close();
});

test("someone signed in is not asked about first and keeps the widget's own answer", async () => {
    const { page, asked } = await mount({ anonymous: false, schema: refusal });
    await page.locator("interactive-report").waitFor();
    assert.equal(await page.locator(".app-block-signin").count(), 0);
    assert.equal(asked.filter(url => url.includes("/schema")).length <= 1, true);
    await page.close();
});

test("the note about saving views comes from the server's answer, for whoever it answers", async () => {
    const setupUrl = "/sub/umbraco/section/settings/workspace/appblocks/view/setup";
    const told = await mount({ anonymous: false, setupNote: { contentType: "application/json", body: JSON.stringify({ setupUrl }) } });
    const note = told.page.locator("section.app-block-report > .app-report-setup-note[role=status]");
    await note.waitFor();
    assert.match(await note.textContent(), /Saving report views is off because its database tables aren't set up\./);
    assert.equal(await note.getByRole("link", { name: "Open database setup" }).getAttribute("href"), setupUrl);
    await told.page.locator("interactive-report").waitFor();
    assert.deepEqual(told.errors, []);
    await told.page.close();

    // Everyone else is answered with nothing, and a page without the attribute does not even ask.
    const reader = await mount({ anonymous: false, setupNote: { status: 204 } });
    await reader.page.locator("interactive-report").waitFor();
    assert.ok(reader.asked.some(url => url.endsWith("/__saved-views-setup")));
    assert.equal(await reader.page.locator(".app-report-setup-note").count(), 0);
    await reader.page.close();

    const quiet = await mount({ anonymous: false });
    await quiet.page.locator("interactive-report").waitFor();
    assert.ok(!quiet.asked.some(url => url.endsWith("/__saved-views-setup")));
    await quiet.page.close();
});

test("a report creates and edits in its side panel while the master page keeps its draft and query", async () => {
    const id = "app-form-lines", base = "/sub/orders?customer=7", assets = "/sub/App_Plugins/ApplicationBlocks/";
    const schema = {
        title: "Lines", defaultState: reportState(), columns: [{ name: "ID", label: "ID", type: "number" }],
        limits: { defaultPageSize: 25, maxPageSize: 100 }, features: [], capabilities: { aggregateFunctions: {}, expressionFunctions: [] },
        editLink: { urlTemplate: base + "&lineId={ID}#" + id, label: "Edit line", mode: "event" },
        createLink: { url: base + "#" + id, label: "New line", mode: "event" },
    };
    const calls = [], queries = [];
    const record = key => ({ key, values: { title: key ? "Line " + key : "" }, checksum: "v1", capabilities: { create: true, update: true, delete: true } });
    const { page, errors } = await mount({ anonymous: false, panels: id, schema: { contentType: "application/json", body: JSON.stringify(schema) },
        html: url => `<link rel="stylesheet" href="${assets}runtime.css"><input aria-label="Master draft"><div><button data-open-panel="${id}">Line</button></div>
        <dialog class="app-form-panel" data-app-form-panel data-render-query="true" id="${id}" aria-label="Line"><header class="app-panel-header"><button data-panel-close>Close</button></header>
            <form data-app-form data-api="/sub/api/forms/p/f" data-key-parameter="lineId" data-return="/elsewhere">
                <div class="app-form-message"></div><div class="app-form-errors" hidden></div>
                <select aria-label="Choices"><option>${url.searchParams.has("lineId") ? "Existing line options" : "New line options"}</option></select>
                <fieldset class="app-form-body" disabled><input aria-label="Title" id="title" data-field="title"><button data-save>Save</button>
                <a href="/elsewhere" data-app-cancel>Cancel</a><button type="button" data-delete>Delete</button></fieldset>
            </form></dialog><script type="module" src="${assets}runtime.js"></script>`,
        prepare: async page => {
            await page.route("**/App_Plugins/ApplicationBlocks/*", async route => {
                const name = new URL(route.request().url()).pathname;
                if (name.endsWith("/reports.js") || name.endsWith("/report-print.js")) return route.fallback();
                return route.fulfill(await runtimeAsset(name));
            });
            await page.route("**/api/reports/orders/query*", route => {
                queries.push(route.request().postDataJSON());
                return route.fulfill({ json: hydratedResult() });
            });
            await page.route("**/api/forms/**", route => {
                const request = route.request(), url = new URL(request.url());
                calls.push([request.method(), url.pathname, url.search]);
                if (request.method() === "DELETE") return route.fulfill({ status: 204 });
                if (request.postDataJSON()?.values.title === "Invalid") return route.fulfill({ status: 422, json: { errors: { title: ["Choose another title."] } } });
                return route.fulfill({ json: { record: record(request.method() === "POST" ? "2" : url.searchParams.get("lineId")), definitionVersion: "v1", antiforgeryToken: "token" } });
            });
        },
    });
    const panel = page.getByRole("dialog"), title = panel.getByLabel("Title");
    const create = page.getByRole("button", { name: "New line" }), edit = page.getByRole("button", { name: "Edit line" });
    await create.waitFor();
    assert.deepEqual(calls, []);
    await page.getByLabel("Master draft").fill("Unsaved master");
    await edit.click();
    await page.waitForFunction(() => document.querySelector("dialog [data-field]")?.value === "Line 1");
    assert.equal(await panel.getByLabel("Choices").inputValue(), "Existing line options");
    await panel.evaluate(node => Promise.all(node.getAnimations().map(animation => animation.finished)));
    const bounds = await panel.boundingBox();
    assert.equal(bounds.x + bounds.width, page.viewportSize().width);
    assert.ok(bounds.width < page.viewportSize().width);
    await title.fill("Discarded");
    await panel.getByText("Cancel").click();
    await create.click();
    await title.fill("Invalid");
    assert.equal(await panel.getByLabel("Choices").inputValue(), "New line options");
    await panel.getByText("Save").click();
    await panel.getByText("Choose another title.").waitFor();
    assert.equal(await title.inputValue(), "Invalid");
    await title.fill("A new line");
    await panel.getByText("Save").click();
    await panel.waitFor({ state: "hidden" });
    await edit.click();
    await title.fill("Changed");
    await panel.getByText("Save").click();
    await panel.waitFor({ state: "hidden" });
    await edit.click();
    page.once("dialog", dialog => dialog.accept());
    const refreshed = page.waitForRequest(req => new URL(req.url()).pathname.endsWith("/orders/query"));
    await panel.getByText("Delete").click();
    await refreshed;
    await panel.waitFor({ state: "hidden" });
    assert.equal(page.url(), "http://app.test" + base);
    assert.equal(await page.getByLabel("Master draft").inputValue(), "Unsaved master");
    assert.deepEqual(calls.map(([method]) => method), ["GET", "GET", "POST", "POST", "GET", "PUT", "GET", "DELETE"]);
    assert.ok(calls.every(([, , search]) => new URLSearchParams(search).get("customer") === "7"));
    assert.ok(calls.slice(1, 4).every(([, , search]) => !new URLSearchParams(search).has("lineId")));
    assert.equal(queries.length, 3);
    // Arriving from a picked page, or opening the same destination in a new tab, opens only the named panel.
    await page.setViewportSize({ width: 375, height: 700 });
    await page.goto("http://app.test/sub/orders?lineId=2#" + id);
    await page.waitForFunction(() => document.querySelector("dialog [data-field]")?.value === "Line 2");
    assert.ok(Math.abs((await panel.boundingBox()).width - 375) < 0.5, "The panel fills the mobile viewport");
    await page.keyboard.press("Escape");
    await panel.waitFor({ state: "hidden" });
    await page.waitForURL(url => url.hash === "");
    assert.deepEqual(errors, []);
    await page.close();
});
