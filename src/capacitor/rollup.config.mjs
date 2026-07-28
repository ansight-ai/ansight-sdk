import json from '@rollup/plugin-json';
import { nodeResolve } from '@rollup/plugin-node-resolve';
import typescript from '@rollup/plugin-typescript';

const plugins = () => [
  nodeResolve(),
  json(),
  typescript({
    tsconfig: './tsconfig.json',
    declaration: false,
    declarationMap: false,
    outDir: undefined,
  }),
];

export default [
  {
    input: 'src/index.ts',
    external: ['@capacitor/core'],
    plugins: plugins(),
    output: [
      {
        file: 'dist/plugin.js',
        format: 'iife',
        name: 'capacitorAnsight',
        exports: 'named',
        globals: { '@capacitor/core': 'capacitorExports' },
        sourcemap: true,
      },
      {
        file: 'dist/plugin.cjs.js',
        format: 'cjs',
        exports: 'named',
        sourcemap: true,
      },
    ],
  },
  {
    input: 'src/standalone.ts',
    plugins: plugins(),
    output: {
      file: 'dist/standalone.js',
      format: 'iife',
      name: 'capacitorAnsightStandalone',
      sourcemap: true,
    },
  },
];
