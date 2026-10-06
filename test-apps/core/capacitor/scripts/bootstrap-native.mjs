import { existsSync } from 'node:fs';
import { spawnSync } from 'node:child_process';

for (const platform of ['android', 'ios']) {
  if (existsSync(platform)) continue;
  const result = spawnSync('npx', ['cap', 'add', platform], {
    stdio: 'inherit',
    shell: process.platform === 'win32',
  });
  if (result.status !== 0) process.exit(result.status ?? 1);
}
