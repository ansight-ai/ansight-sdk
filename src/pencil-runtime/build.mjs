import {readFileSync, writeFileSync} from 'node:fs';
const source = readFileSync(new URL('./index.js', import.meta.url), 'utf8');
const footer = 'module.exports = { createPencilRuntime, pointerProperties, validateSequence };';
if (!source.includes(footer)) throw new Error('Update the Pencil ESM export generation for the changed public API.');
writeFileSync(new URL('./index.mjs', import.meta.url), source.replace(footer, 'export { createPencilRuntime, pointerProperties, validateSequence };'));
