#!/usr/bin/env node

import { execFileSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const sdkRoot = resolve(scriptDirectory, '..');
const manifest = JSON.parse(
  readFileSync(join(sdkRoot, 'test-apps', 'capacitor-apps.json'), 'utf8'),
);
const rootArgument = process.argv.find((value) => value.startsWith('--root='));
const corpusRoot = resolve(
  rootArgument?.slice('--root='.length) ??
    join(sdkRoot, '..', 'ansight-sdk-test-apps', 'capacitor'),
);
const failures = [];
let moduleCount = 0;
let staticCount = 0;

for (const app of manifest.apps) {
  const appRoot = join(corpusRoot, app.repository.replace('/', '__'));
  const packagePath = join(appRoot, 'package.json');
  const metadataPath = join(appRoot, 'meta-data.json');
  if (!existsSync(packagePath) || !existsSync(metadataPath)) {
    failures.push(`${app.repository}: prepared package or metadata is missing`);
    continue;
  }

  const head = output('git', ['rev-parse', 'HEAD'], appRoot);
  const packageJson = JSON.parse(readFileSync(packagePath, 'utf8'));
  const metadata = JSON.parse(readFileSync(metadataPath, 'utf8'));
  const dependency = packageJson.dependencies?.['@ansight/capacitor'];

  if (head !== app.commit) failures.push(`${app.repository}: HEAD is ${head}, expected ${app.commit}`);
  if (metadata.source?.pinnedCommit !== app.commit) {
    failures.push(`${app.repository}: metadata commit does not match the manifest`);
  }
  if (metadata.source?.license !== app.license) {
    failures.push(`${app.repository}: metadata license does not match the manifest`);
  }
  if (typeof dependency !== 'string' || !dependency.startsWith('file:')) {
    failures.push(`${app.repository}: local @ansight/capacitor dependency is missing`);
  }

  const injection = metadata.local?.injection;
  if (typeof injection !== 'string' || injection === 'manual') {
    failures.push(`${app.repository}: bootstrap injection is not configured`);
    continue;
  }
  if (injection.startsWith('static:')) {
    staticCount += 1;
    for (const htmlFile of injection.slice('static:'.length).split(',')) {
      const html = readFileSync(join(appRoot, htmlFile), 'utf8');
      const standalone = join(dirname(join(appRoot, htmlFile)), '.ansight-capacitor', 'bootstrap.js');
      if (!html.includes('data-ansight-capacitor-bootstrap') || !existsSync(standalone)) {
        failures.push(`${app.repository}: static bootstrap is incomplete for ${htmlFile}`);
      }
    }
  } else {
    moduleCount += 1;
    const source = readFileSync(join(appRoot, injection), 'utf8');
    if (!source.includes('.ansight-capacitor/bootstrap')) {
      failures.push(`${app.repository}: module bootstrap import is missing from ${injection}`);
    }
  }
}

if (failures.length > 0) {
  console.error(failures.join('\n'));
  process.exit(1);
}

console.log(
  `Verified ${manifest.apps.length} pinned Capacitor apps (${moduleCount} module, ${staticCount} static) in ${corpusRoot}`,
);

function output(command, args, cwd) {
  return execFileSync(command, args, { cwd, encoding: 'utf8' }).trim();
}
