// The single source of truth for the visible PlanetForge release version.
// Every deployed build also displays its own Git commit SHA in the page badge.
const buildVersion = '0.0.38.3';
document.title = `PlanetForge ${buildVersion} — Climate Experiment`;

document.addEventListener('DOMContentLoaded', () => {
    const badge = document.getElementById('planetforge-build-badge');
    if (badge) badge.textContent = badge.textContent.replace('__BUILD_VERSION__', buildVersion);
});
