// Eklentiyi Chrome/Edge ve Firefox için paketler (bağımlılık yok).
//   node build.mjs          → dist/chrome, dist/firefox
// Chrome/Edge: "Paketlenmemiş öğe yükle" ile dist/chrome klasörü yüklenir.
// Firefox: about:debugging → "Geçici eklenti yükle" ile dist/firefox/manifest.json seçilir.
import { cpSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = dirname(fileURLToPath(import.meta.url));
const base = JSON.parse(readFileSync(join(root, 'manifest.base.json'), 'utf8'));

const targets = {
  chrome: {
    ...base,
    background: { service_worker: 'background.js' },
    minimum_chrome_version: '116',
  },
  firefox: (() => {
    const { key, ...rest } = base;   // "key" yalnızca Chrome kimliği içindir
    return {
      ...rest,
      background: { scripts: ['lib/protocol.js', 'background.js'] },
      browser_specific_settings: {
        gecko: { id: 'localvault@local.vault', strict_min_version: '128.0' },
      },
    };
  })(),
};

for (const [name, manifest] of Object.entries(targets)) {
  const out = join(root, 'dist', name);
  rmSync(out, { recursive: true, force: true });
  mkdirSync(out, { recursive: true });
  cpSync(join(root, 'src'), out, { recursive: true });
  writeFileSync(join(out, 'manifest.json'), JSON.stringify(manifest, null, 2) + '\n');
  console.log(`✓ dist/${name}`);
}
