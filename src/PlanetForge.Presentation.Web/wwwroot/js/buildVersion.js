const buildVersion = '0.0.3.9';
const buildLabel = 'LOAD HOTFIX';
const pageTitle = `PlanetForge ${buildVersion} — Load Hotfix`;
const maximumAttempts = 100;
const retryDelayMilliseconds = 100;

document.title = pageTitle;
applyBuildVersion(0);

function applyBuildVersion(attempt) {
    const milestone = Array.from(document.querySelectorAll('.eyebrow'))
        .find(element => element.textContent?.trim().startsWith('MILESTONE 0.0.3'));
    const seedChip = document.querySelector('.seed-chip');

    if (milestone && seedChip) {
        milestone.textContent = `MILESTONE ${buildVersion} · ${buildLabel}`;
        const seedMatch = seedChip.textContent?.match(/SEED\s+(.+)$/);
        const seed = seedMatch?.[1]?.trim();
        seedChip.textContent = seed ? `BUILD ${buildVersion} · SEED ${seed}` : `BUILD ${buildVersion}`;
        return;
    }

    if (attempt < maximumAttempts) {
        window.setTimeout(() => applyBuildVersion(attempt + 1), retryDelayMilliseconds);
    }
}
