import assert from 'node:assert/strict';
import { runtimeAsset } from './runtime-assets.mjs';
import { test, before, after } from 'node:test';
import { createRequire } from 'node:module';
import { readFile } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../../', import.meta.url));
const require = createRequire(resolve(process.env.INTERACTIVE_REPORTS_ROOT || resolve(root, '../AurorisNet.InteractiveReports'), 'package.json'));
const { chromium } = require('playwright');
const styles = await readFile(resolve(root, 'src/AppBlocks/wwwroot/App_Plugins/ApplicationBlocks/runtime.css'), 'utf8');
let browser;
before(async () => { browser = await chromium.launch({ headless: true, ...(existsSync(chromium.executablePath()) ? {} : { executablePath: process.env.BROWSER_PATH || 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe' }) }); });
after(async () => browser?.close());

function escapeAttr(value) {
    return String(value ?? '').replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

function emailBlock(id, label, { to = '', cc = '', bcc = '', subject = '', body = '', linkText = '', emptyText = '' }) {
    return `<div class="app-form-field">
        <label id="${id}-label">${label}</label>
        <a id="${id}-link" class="app-email-link" data-email-link
            data-to="${escapeAttr(to)}" data-cc="${escapeAttr(cc)}" data-bcc="${escapeAttr(bcc)}"
            data-subject="${escapeAttr(subject)}" data-body="${escapeAttr(body)}"
            data-link-text="${escapeAttr(linkText)}" data-empty-text="${escapeAttr(emptyText)}"
            aria-labelledby="${id}-label ${id}-link"></a>
    </div>`;
}

async function mount({ values = {}, blocks = [] } = {}) {
    const page = await browser.newPage({ viewport: { width: 720, height: 480 } });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    const record = { key: '7', checksum: '1', values, capabilities: { create: true, update: true } };
    await page.route('**/*', async route => {
        const url = new URL(route.request().url());
        const asset = await runtimeAsset(url.pathname); if (asset) return route.fulfill(asset);
        if (url.pathname.startsWith('/api/forms/')) {
            return route.fulfill({ json: { record, definitionVersion: '1', antiforgeryToken: 'token' } });
        }
        return route.fulfill({ contentType: 'text/html', body: `<!doctype html><html lang="en"><meta name="viewport" content="width=device-width"><title>Email links</title>
            <style>body { font-family: system-ui; padding: 1rem; margin: 0; } ${styles}</style>
            <div class="application-blocks"><form class="app-form" data-app-form data-api="/api/forms/page/form" data-key-parameter="id" data-return="/" novalidate>
                <div class="app-form-message" role="status" aria-live="polite"></div><div class="app-form-errors" role="alert" tabindex="-1" hidden></div>
                <fieldset class="app-form-body" disabled>
                    <input type="hidden" id="email" name="email" data-field="email" data-kind="text" data-readonly="true">
                    <input type="hidden" id="ccEmail" name="ccEmail" data-field="ccEmail" data-kind="text" data-readonly="true">
                    <input type="hidden" id="reference" name="reference" data-field="reference" data-kind="text" data-readonly="true">
                    <div class="app-form-field"><label for="actionRequired">Action required</label><textarea id="actionRequired" name="actionRequired" data-field="actionRequired" data-kind="text" data-readonly="false"></textarea></div>
                    ${blocks.join('')}
                    <div class="app-form-actions"><button type="submit" data-save>Save</button></div>
                </fieldset>
            </form></div><script type="module" src="/App_Plugins/ApplicationBlocks/runtime.js"></script></html>` });
    });
    await page.goto('http://localhost/case/?id=7');
    await page.waitForFunction(() => !document.querySelector('.app-form-body').disabled);
    return { page, errors };
}

test('appEmailLink builds templated mailto href, omits empty body lines, deduplicates recipients, and updates live', async () => {
    const { page, errors } = await mount({
        values: {
            email: 'jane+ops@example.org',
            ccEmail: 'JANE+ops@example.org; lead@example.org',
            reference: 'REQ-2026-0042',
            actionRequired: '',
        },
        blocks: [
            emailBlock('notify', 'Email requester', {
                to: '{email}',
                cc: '{ccEmail}',
                subject: '[{reference}] Update ({constructor})',
                body: 'Status: Open\nAction required: {actionRequired}\n\nCase: {reference}',
                emptyText: 'No email on file',
            }),
        ],
    });
    const link = page.locator('#notify-link');
    assert.equal(await link.textContent(), 'jane+ops@example.org');
    const initialHref = await link.getAttribute('href');
    assert.ok(initialHref.startsWith('mailto:jane+ops@example.org?cc=lead@example.org&subject='));
    const decodedInitial = decodeURIComponent(initialHref);
    assert.ok(decodedInitial.includes('[REQ-2026-0042] Update ()'));
    assert.ok(!decodedInitial.includes('Action required:'));
    assert.ok(decodedInitial.includes('Status: Open\r\n\r\nCase: REQ-2026-0042'));

    await page.locator('#actionRequired').fill('Attach signed page 2.');
    const updatedHref = await link.getAttribute('href');
    assert.ok(decodeURIComponent(updatedHref).includes('Action required: Attach signed page 2.'));
    assert.deepEqual(errors, []);
    await page.close();
});

test('appEmailLink removes href and shows emptyText when To resolves empty', async () => {
    const { page, errors } = await mount({
        values: { email: '', reference: 'REQ-1' },
        blocks: [
            emailBlock('notify', 'Email requester', {
                to: '{email}',
                subject: '[{reference}] Update',
                emptyText: 'No email on file',
            }),
        ],
    });
    const link = page.locator('#notify-link');
    assert.equal(await link.getAttribute('href'), null);
    assert.equal(await link.textContent(), 'No email on file');
    assert.deepEqual(errors, []);
    await page.close();
});
