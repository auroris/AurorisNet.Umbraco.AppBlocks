import { readFile, writeFile } from 'node:fs/promises';

const modules = new URL('node_modules/', import.meta.url);
const output = new URL('../../src/AppBlocks/wwwroot/App_Plugins/ApplicationBlocks/', import.meta.url);
const read = path => readFile(new URL(path, modules), 'utf8');
const write = (name, contents) => writeFile(new URL(name, output), contents);

// Use the upstream browser module and stylesheet. Sites serve both from the AppBlocks package.
const js = await read('tom-select/dist/esm-browser/tom-select.popular.js');
const css = await read('tom-select/dist/css/tom-select.min.css');
await write('tom-select.js', js.replace(/^\/\/# sourceMappingURL=.*$/m, ''));
await write('tom-select.css', css.replace(/\/\*# sourceMappingURL=.*?\*\//g, ''));
const sifter = (await read('@orchidjs/sifter/README.md')).split('## License')[1].trim();
await write('tom-select-LICENSE.txt', [
    'Tom Select', await read('tom-select/LICENSE'),
    '@orchidjs/sifter', sifter,
    '@orchidjs/unicode-variants', await read('@orchidjs/unicode-variants/LICENSE'),
].join('\n'));
