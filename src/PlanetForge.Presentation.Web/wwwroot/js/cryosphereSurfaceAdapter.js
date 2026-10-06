import * as surface from './cryosphereSurface.js';
import * as ocean from './oceanDepthSurface.js';

export function initialize(overlayCanvasId, inputCanvasId, snapshot) {
    const adapted = adaptSnapshot(snapshot);
    surface.initialize(overlayCanvasId, inputCanvasId, adapted);
    ocean.initialize(inputCanvasId, adapted);
}

export function setPlanet(snapshot) {
    const adapted = adaptSnapshot(snapshot);
    surface.setPlanet(adapted);
    ocean.setPlanet(adapted);
}

export function dispose() {
    ocean.dispose();
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
