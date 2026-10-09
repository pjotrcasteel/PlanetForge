// The single source of truth for the visible PlanetForge release version.
// Every deployed build also displays its own Git commit SHA in the page badge.
const buildVersion = '0.0.37.5';
document.title = `PlanetForge ${buildVersion} — Geological Terrain Research`;

document.addEventListener('DOMContentLoaded', () => {
    const badge = document.getElementById('planetforge-build-badge');
    if (badge) badge.textContent = badge.textContent.replace('__BUILD_VERSION__', buildVersion);
});
