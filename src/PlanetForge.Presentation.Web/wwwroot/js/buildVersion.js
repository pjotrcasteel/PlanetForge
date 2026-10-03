const buildVersion = '0.0.3.8';
const buildLabel = 'ZOOM STREAM FIX';
const pageTitle = `PlanetForge ${buildVersion} — Zoom Stream Fix`;

function applyBuildVersion() {
    if (document.title !== pageTitle) {
        document.title = pageTitle;
    }

    const milestone = Array.from(document.querySelectorAll('.eyebrow'))
        .find(element => element.textContent?.trim().startsWith('MILESTONE 0.0.3'));

    if (milestone) {
        milestone.textContent = `MILESTONE ${buildVersion} · ${buildLabel}`;
    }

    const seedChip = document.querySelector('.seed-chip');
    if (seedChip) {
        const seedMatch = seedChip.textContent?.match(/SEED\s+(.+)$/);
        const seed = seedMatch?.[1]?.trim();
        seedChip.textContent = seed ? `BUILD ${buildVersion} · SEED ${seed}` : `BUILD ${buildVersion}`;
    }
}

const observer = new MutationObserver(() => applyBuildVersion());
observer.observe(document.documentElement, { childList: true, subtree: true, characterData: true });
applyBuildVersion();
