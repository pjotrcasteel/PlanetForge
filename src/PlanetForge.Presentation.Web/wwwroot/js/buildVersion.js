const buildVersion = '0.0.8';
const buildCommit = '__BUILD_SHA__';
const shortCommit = buildCommit === '__BUILD_SHA__' ? 'dev' : buildCommit.slice(0, 7);
const pageTitle = `PlanetForge ${buildVersion} — Terraforming Foundation`;

document.title = pageTitle;
window.setTimeout(() => document.title = pageTitle, 500);
window.setTimeout(() => document.title = pageTitle, 2_000);

const buildBadge = document.createElement('div');
buildBadge.id = 'planetforge-build-badge';
buildBadge.textContent = `PF ${buildVersion} · ${shortCommit}`;
buildBadge.style.position = 'fixed';
buildBadge.style.left = '50%';
buildBadge.style.bottom = 'max(4px, env(safe-area-inset-bottom))';
buildBadge.style.transform = 'translateX(-50%)';
buildBadge.style.zIndex = '1000';
buildBadge.style.font = '600 9px/1.2 ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace';
buildBadge.style.letterSpacing = '0.08em';
buildBadge.style.color = 'rgba(210, 225, 220, 0.45)';
buildBadge.style.pointerEvents = 'none';
buildBadge.style.userSelect = 'none';
document.body.appendChild(buildBadge);