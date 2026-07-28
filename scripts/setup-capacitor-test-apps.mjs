#!/usr/bin/env node

import { copyFileSync, existsSync, readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { dirname, join, relative, resolve, sep } from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const sdkRoot = resolve(scriptDirectory, '..');
const manifestPath = join(sdkRoot, 'test-apps', 'capacitor-apps.json');
const manifest = JSON.parse(readFileSync(manifestPath, 'utf8'));
const rootArgument = process.argv.find((value) => value.startsWith('--root='));
const corpusRoot = resolve(
  rootArgument?.slice('--root='.length) ??
    join(sdkRoot, '..', 'ansight-sdk-test-apps', 'capacitor'),
);
const shouldInstall = process.argv.includes('--install');
const cloneOnly = process.argv.includes('--clone-only');

const entryCandidates = [
  'src/main.ts',
  'src/main.tsx',
  'src/main.js',
  'src/main.jsx',
  'src/index.ts',
  'src/index.tsx',
  'src/index.js',
  'src/index.jsx',
  'app/layout.tsx',
  'src/app/layout.tsx',
  'pages/_app.tsx',
  'src/pages/_app.tsx',
];

mkdirSync(corpusRoot, { recursive: true });

for (const [index, app] of manifest.apps.entries()) {
  const folder = app.repository.replace('/', '__');
  const appRoot = join(corpusRoot, folder);
  process.stdout.write(`[${index + 1}/${manifest.apps.length}] ${app.repository}\n`);
  cloneAtCommit(app, appRoot);
  if (!cloneOnly) prepareApp(app, appRoot);
  if (shouldInstall) installApp(app, appRoot);
}

const summary = {
  schema: manifest.schema,
  preparedAtUtc: new Date().toISOString(),
  sdkRoot,
  corpusRoot,
  count: manifest.apps.length,
  apps: manifest.apps.map((app) => ({
    repository: app.repository,
    folder: app.repository.replace('/', '__'),
    commit: app.commit,
    license: app.license,
    capacitorCore: app.capacitorCore,
    category: app.category,
  })),
};
writeFileSync(join(corpusRoot, 'corpus.json'), `${JSON.stringify(summary, null, 2)}\n`);
process.stdout.write(`Prepared ${manifest.apps.length} Capacitor apps in ${corpusRoot}\n`);

function cloneAtCommit(app, appRoot) {
  if (!existsSync(join(appRoot, '.git'))) {
    run(
      'git',
      [
        'clone',
        '--filter=blob:none',
        '--no-tags',
        '--depth',
        '1',
        '--branch',
        app.branch,
        `https://github.com/${app.repository}.git`,
        appRoot,
      ],
      sdkRoot,
    );
  }

  const currentCommit = output('git', ['rev-parse', 'HEAD'], appRoot);
  if (currentCommit !== app.commit) {
    run('git', ['fetch', '--depth', '1', 'origin', app.commit], appRoot);
    run('git', ['checkout', '--detach', app.commit], appRoot);
  }
}

function prepareApp(app, appRoot) {
  const packagePath = join(appRoot, 'package.json');
  const packageJson = JSON.parse(readFileSync(packagePath, 'utf8'));
  packageJson.dependencies = {
    ...(packageJson.dependencies ?? {}),
    '@ansight/capacitor': `file:${join(sdkRoot, 'src', 'capacitor')}`,
  };
  writeFileSync(packagePath, `${JSON.stringify(packageJson, null, 2)}\n`);

  const sidecarRoot = join(appRoot, '.ansight-capacitor');
  mkdirSync(sidecarRoot, { recursive: true });
  writeFileSync(
    join(sidecarRoot, 'bootstrap.ts'),
    `import Ansight from '@ansight/capacitor';

if (typeof window !== 'undefined') {
  void Ansight.initializeAndActivate(
    Ansight.createOptionsBuilder()
      .withAnsightDefaults()
      .withAllToolAccess()
      .withVisualTreeTools()
      .withFileSystemTools()
      .withDatabaseTools()
      .withPreferencesTools()
      .withReflectionTools()
      .withDomTools()
      .withErrorCapture()
      .registerCustomProperty('testCorpus', 'repository', '${app.repository}')
      .registerCustomProperty('testCorpus', 'category', '${app.category}')
      .build(),
  ).catch((error) => console.error('[Ansight Capacitor]', error));
}
`,
  );

  const entry =
    app.entry ?? entryCandidates.find((candidate) => existsSync(join(appRoot, candidate)));
  const injection = app.staticHtml
    ? injectStandalone(app, appRoot)
    : entry
      ? injectBootstrap(appRoot, entry, sidecarRoot)
      : 'manual';
  const metadata = {
    schema: 'ai.ansight.sdk-test-app.metadata.v1',
    source: {
      repository: app.repository,
      repositoryUrl: `https://github.com/${app.repository}`,
      license: app.license,
      pinnedCommit: app.commit,
      branch: app.branch,
      category: app.category,
      capacitorCore: app.capacitorCore,
    },
    local: {
      folder: app.repository.replace('/', '__'),
      path: appRoot,
      packageManager: app.packageManager,
      ansightDependency: `file:${join(sdkRoot, 'src', 'capacitor')}`,
      injection,
    },
    ansight: {
      coverage: [
        'runtime-lifecycle',
        'telemetry',
        'screen-and-route',
        'native-visual-tree',
        'dom-visual-tree',
        'filesystem-tools',
        'database-tools',
        'preferences-tools',
        'reflection-tools',
        'secure-storage-tools',
        'error-capture',
        'host-auto-probe',
      ],
      harness: join(sdkRoot, 'src', 'capacitor', 'example-app'),
    },
  };
  writeFileSync(join(appRoot, 'meta-data.json'), `${JSON.stringify(metadata, null, 2)}\n`);
  writeFileSync(
    join(sidecarRoot, 'README.md'),
    `# Ansight Capacitor test adapter

This adapter links \`@ansight/capacitor\` from:

\`${join(sdkRoot, 'src', 'capacitor')}\`

Pinned upstream: [${app.repository}](https://github.com/${app.repository}/tree/${app.commit})
at \`${app.commit}\`. License: \`${app.license}\`.

Bootstrap injection: \`${injection}\`.

Install with the repository's package manager, build its web bundle, then run
\`npx cap sync\` and the normal \`npx cap run android|ios\` workflow. Use the
first-party harness for destructive pairing/session checks.
`,
  );
}

function injectBootstrap(appRoot, entry, sidecarRoot) {
  const entryPath = join(appRoot, entry);
  if (!existsSync(entryPath)) {
    throw new Error(`Configured entry '${entry}' does not exist in ${appRoot}`);
  }
  const source = readFileSync(entryPath, 'utf8');
  if (source.includes('.ansight-capacitor/bootstrap')) return entry;
  let importPath = relative(dirname(entryPath), join(sidecarRoot, 'bootstrap'))
    .split(sep)
    .join('/');
  if (!importPath.startsWith('.')) importPath = `./${importPath}`;
  const importLine = `import '${importPath}';\n`;
  if (entry.endsWith('.vue') || entry.endsWith('.svelte')) {
    const script = source.match(/<script(?:\s[^>]*)?>/);
    const updated = script
      ? `${source.slice(0, script.index + script[0].length)}\n${importLine}${source.slice(script.index + script[0].length)}`
      : `<script lang="ts">\n${importLine}</script>\n\n${source}`;
    writeFileSync(entryPath, updated);
    return entry;
  }
  const directive = source.match(/^(['"])use client\1;\s*/);
  const insertion = directive ? directive[0].length : 0;
  const updated = `${source.slice(0, insertion)}${insertion ? '\n' : ''}${importLine}${source.slice(insertion)}`;
  writeFileSync(entryPath, updated);
  return entry;
}

function injectStandalone(app, appRoot) {
  const htmlFiles = Array.isArray(app.staticHtml) ? app.staticHtml : [app.staticHtml];
  const standaloneSource = join(sdkRoot, 'src', 'capacitor', 'dist', 'standalone.js');
  if (!existsSync(standaloneSource)) {
    throw new Error(
      `Missing ${standaloneSource}. Run 'npm run build' in src/capacitor before preparing static apps.`,
    );
  }

  for (const htmlFile of htmlFiles) {
    const htmlPath = join(appRoot, htmlFile);
    if (!existsSync(htmlPath)) {
      throw new Error(`Configured HTML entry '${htmlFile}' does not exist in ${appRoot}`);
    }
    const assetDirectory = join(dirname(htmlPath), '.ansight-capacitor');
    mkdirSync(assetDirectory, { recursive: true });
    copyFileSync(standaloneSource, join(assetDirectory, 'bootstrap.js'));

    const html = readFileSync(htmlPath, 'utf8');
    if (html.includes('data-ansight-capacitor-bootstrap')) continue;
    const configuration = JSON.stringify({
      customProperties: {
        testCorpus: {
          repository: app.repository,
          category: app.category,
        },
      },
    }).replaceAll('<', '\\u003c');
    const tags = `  <script data-ansight-capacitor-bootstrap>globalThis.__ANSIGHT_CAPACITOR_STANDALONE_OPTIONS__ = ${configuration};</script>
  <script src=".ansight-capacitor/bootstrap.js"></script>
`;
    const updated = html.includes('</head>')
      ? html.replace('</head>', `${tags}</head>`)
      : html.replace('</body>', `${tags}</body>`);
    writeFileSync(htmlPath, updated);
  }
  return `static:${htmlFiles.join(',')}`;
}

function installApp(app, appRoot) {
  const command =
    app.packageManager === 'pnpm'
      ? ['corepack', ['pnpm', 'install']]
      : existsSync(join(appRoot, 'yarn.lock'))
        ? ['corepack', ['yarn', 'install']]
        : ['npm', ['install']];
  run(command[0], command[1], appRoot);
  if (existsSync(join(appRoot, 'android')) || existsSync(join(appRoot, 'ios'))) {
    run('npx', ['cap', 'sync'], appRoot);
  }
}

function run(command, args, cwd) {
  const result = spawnSync(command, args, { cwd, stdio: 'inherit', shell: false });
  if (result.status !== 0) {
    throw new Error(`${command} ${args.join(' ')} failed in ${cwd}`);
  }
}

function output(command, args, cwd) {
  const result = spawnSync(command, args, { cwd, encoding: 'utf8', shell: false });
  if (result.status !== 0) {
    throw new Error(`${command} ${args.join(' ')} failed in ${cwd}`);
  }
  return result.stdout.trim();
}
