// Deterministic, duration-aware file partitions for the CI browser gate.
// Every .spec.mjs must appear exactly once, including production-only smoke
// specs (those remain Playwright-skipped outside production verification).
// Update this table when adding files; unknown specs fail closed.
import { spawnSync } from 'node:child_process';
import { readdirSync } from 'node:fs';
import { dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const directory = dirname(fileURLToPath(import.meta.url));
const groups = [
  ['01-main-planet-geology.spec.mjs'],
  ['foundation.spec.mjs', 'airless-reference.spec.mjs', 'regional-rock.spec.mjs', 'formation-simulation.spec.mjs'],
  [
    '00-geology-preview.spec.mjs',
    'camera-region-streaming.spec.mjs',
    'deployment.spec.mjs',
    'geological-archives.spec.mjs',
    'magma-ocean.spec.mjs',
    'mobile-gestures.spec.mjs',
    'rock-columns.spec.mjs',
    'rock-diagnostic-maps.spec.mjs',
    'terrain-geometry-cache.spec.mjs'
  ]
];

const discovered = readdirSync(directory).filter(name => name.endsWith('.spec.mjs')).sort();
const assigned = groups.flat().sort();
if (discovered.length !== assigned.length || discovered.some((name, index) => name !== assigned[index])) {
  console.error('Browser shard inventory does not match actual *.spec.mjs files.');
  console.error('Discovered:', discovered);
  console.error('Assigned:', assigned);
  process.exit(1);
}

if (process.argv[2] === '--verify') {
  for (let i = 0; i < groups.length; i++) console.log(`Shard ${i + 1}: ${groups[i].join(', ')}`);
  process.exit(0);
}

const shard = Number(process.argv[2]);
if (!Number.isInteger(shard) || shard < 1 || shard > groups.length) {
  console.error('Expected a browser shard number between 1 and 3.');
  process.exit(1);
}

const executable = process.platform === 'win32' ? 'npx.cmd' : 'npx';
const result = spawnSync(executable, ['playwright', 'test', ...groups[shard - 1]], {
  cwd: directory,
  stdio: 'inherit',
  env: process.env
});
if (result.error) console.error(result.error);
process.exit(result.status ?? 1);
