const buildVersion = '0.0.6';
const buildLabel = 'PLANET RUN';
const pageTitle = `PlanetForge ${buildVersion} — Planet Run`;
const maximumAttempts = 100;
const retryDelayMilliseconds = 100;

document.title = pageTitle;
applyBuildVersion(0);

function applyBuildVersion(attempt) {
    const milestone = Array.from(document.querySelectorAll('.eyebrow'))
        .find(element => element.textContent?.trim().startsWith('MILESTONE'));
    const seedChip = document.querySelector('.seed-chip');

    if (milestone && seedChip) {
        milestone.textContent = `MILESTONE ${buildVersion} · ${buildLabel}`;
        const seedMatch = seedChip.textContent?.match(/SEED\s+(.+)$/);
        const seed = seedMatch?.[1]?.trim();
        seedChip.textContent = seed ? `BUILD ${buildVersion} · SEED ${seed}` : `BUILD ${buildVersion}`;
        document.title = pageTitle;
        window.setTimeout(() => document.title = pageTitle, 500);
        window.setTimeout(() => document.title = pageTitle, 2_000);
        return;
    }

    if (attempt < maximumAttempts) {
        window.setTimeout(() => applyBuildVersion(attempt + 1), retryDelayMilliseconds);
    }
}