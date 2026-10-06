import * as surface from './cryosphereSurface.js';
import * as ocean from './oceanDepthSurface.js';
import * as coast from './coastalReliefSurface.js';

export function initialize(overlayCanvasId, inputCanvasId, snapshot) {
    ensureCoastalCanvas(inputCanvasId);
    const adapted = adaptSnapshot(snapshot);
    surface.initialize(overlayCanvasId, inputCanvasId, adapted);
    ocean.initialize(inputCanvasId, adapted);
    coast.initialize(inputCanvasId, adapted);
}

export function setPlanet(snapshot) {
    const adapted = adaptSnapshot(snapshot);
    surface.setPlanet(adapted);
    ocean.setPlanet(adapted);
    coast.setPlanet(adapted);
}

export function dispose() {
    coast.dispose();
    document.getElementById('coastal-relief-canvas')?.remove();
    ocean.dispose();
    surface.dispose();
}

function ensureCoastalCanvas(inputCanvasId) {
    if (document.getElementById('coastal-relief-canvas')) return;
    const inputCanvas = document.getElementById(inputCanvasId);
    const parent = inputCanvas?.parentElement;
    if (!parent) return;

    const canvas = document.createElement('canvas');
    canvas.id = 'coastal-relief-canvas';
    canvas.setAttribute('aria-hidden', 'true');
    canvas.style.position = 'absolute';
    canvas.style.inset = '0';
    canvas.style.width = '100%';
    canvas.style.height = '100%';
    canvas.style.zIndex = '4';
    canvas.style.pointerEvents = 'none';

    const waterOverlay = document.getElementById('water-overlay-canvas');
    parent.insertBefore(canvas, waterOverlay ?? null);
}

function adaptSnapshot(snapshot) {
    const feedback = snapshot?.climateFeedback;
    if (!feedback) return snapshot;

    const inherited = feedback.landIceFraction ?? feedback.cryosphereFraction;
    if (!Number.isFinite(inherited)) return snapshot;

    const physicalFraction = Math.max(0.0, Math.min(1.0, inherited));
    const visibleLandIceFraction = physicalFraction >= 0.999 ? 1.0 : Math.pow(physicalFraction, 2.2);
    return {
        ...snapshot,
        climateFeedback: {
            ...feedback,
            landIceFraction: visibleLandIceFraction
        }
    };
}
