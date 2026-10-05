import * as surface from './cryosphereSurface.js';

export function initialize(overlayCanvasId, inputCanvasId, snapshot) {
    surface.initialize(overlayCanvasId, inputCanvasId, adaptSnapshot(snapshot));
}

export function setPlanet(snapshot) {
    surface.setPlanet(adaptSnapshot(snapshot));
}

export function dispose() {
    surface.dispose();
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
