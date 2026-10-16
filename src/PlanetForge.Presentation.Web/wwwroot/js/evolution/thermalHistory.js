// Planet-scale (0-D) thermal history that drives the surface evolution model.
// Every quantity is SI unless the name says otherwise. All parameters are
// literature-anchored hypotheses (see PARAMETERS[...].source); none are
// calibrated against a specific Earth reconstruction.
//
// Regimes follow from the state, never from a scripted clock:
//  1. Magma ocean  (crystal fraction < rheological transition): radiative
//     cooling through a convecting, continuously foundering quench crust.
//  2. Stagnant lid / heat pipe: solid-state convection under a conductive lid,
//     heat largely advected by volcanism (Moore & Webb 2013).
//  3. Mobile lid (plate tectonics) once the mantle has cooled enough for
//     subduction to be sustained (Sizova et al. 2010 regime boundaries).

export const SECONDS_PER_YEAR = 3.15576e7;
const SIGMA = 5.670374419e-8;
const GAS_CONSTANT = 8.314462618;

export const PARAMETERS = Object.freeze({
    radiusMeters: 6.371e6,
    mantleMassKilograms: 4.0e24,
    mantleDepthMeters: 2.89e6,
    specificHeat: 1200.0,                 // J/(kg K), silicate
    latentHeat: 4.0e5,                    // J/kg, Elkins-Tanton 2008
    liquidusPotentialKelvin: 2300.0,      // fully molten convecting shell
    solidusPotentialKelvin: 1900.0,       // mantle fully crystalline (potential T)
    rheologicalTransition: 0.40,          // crystal fraction, Abe 1997; Solomatov 2000
    maximumPacking: 0.45,                 // Einstein-Roscoe suspension limit
    initialPotentialKelvin: 2350.0,
    emissivity: 0.9,
    crustConductivity: 2.0,               // W/(m K)
    crustDiffusivity: 7.0e-7,             // m^2/s
    stefanLambda: 0.85,                   // h = 2 lambda sqrt(kappa t); Stefan problem, large undercooling
    thermalExpansivity: 3.0e-5,
    gravity: 9.81,
    meltDensity: 3000.0,
    foundingThicknessMeters: 2.0,         // Loki Patera-scale overturn; hypothesis
    radiativeEquilibriumKelvin: 270.0,    // airless, 1 AU, rock albedo ~0.1
    // Solid-state convection: q = q_ref (dT/dT_ref)^(4/3) (eta_ref/eta)^(1/3).
    presentPotentialKelvin: 1600.0,
    presentMantleHeatFlux: 0.08,          // W/m^2, mantle share of Earth's ~47 TW
    activationEnergy: 3.0e5,              // J/mol, dry olivine diffusion creep
    stagnantLidEfficiency: 0.55,          // stagnant lid + heat pipe vs. mobile lid; hypothesis
    heatPipeShareAtSolidus: 0.85,         // Moore & Webb 2013: heat-pipe Earth
    presentRadiogenicWatts: 2.0e13,       // mantle radiogenic power today
    radiogenicDecayRatio: 4.3,            // power 4.5 Gyr ago / today (U, Th, K budget)
    presentAgeYears: 4.5e9,
    // Sizova et al. 2010: no subduction > +250 K above present Tp; plate-like below.
    plateOnsetPotentialKelvin: 1850.0,
});

export function surfaceArea(p = PARAMETERS) { return 4.0 * Math.PI * p.radiusMeters * p.radiusMeters; }

export function crystalFraction(potentialKelvin, p = PARAMETERS) {
    const span = p.liquidusPotentialKelvin - p.solidusPotentialKelvin;
    return Math.min(1.0, Math.max(0.0, (p.liquidusPotentialKelvin - potentialKelvin) / span));
}

export function rheologicalTransitionKelvin(p = PARAMETERS) {
    return p.liquidusPotentialKelvin - p.rheologicalTransition * (p.liquidusPotentialKelvin - p.solidusPotentialKelvin);
}

// Einstein-Roscoe relative viscosity of a crystal-bearing magma.
export function suspensionViscosityRatio(phi, p = PARAMETERS) {
    const packed = Math.min(phi / p.maximumPacking, 0.995);
    return Math.pow(1.0 - packed, -2.5);
}

export function crustThicknessMeters(ageSeconds, p = PARAMETERS) {
    return 2.0 * p.stefanLambda * Math.sqrt(p.crustDiffusivity * Math.max(ageSeconds, 0.0));
}

// Surface temperature of a conductive crust of thickness h over melt at Tm,
// radiating to space: eps sigma (Ts^4 - Te^4) = k (Tm - Ts) / h.
export function crustSurfaceKelvin(thicknessMeters, meltKelvin, p = PARAMETERS) {
    if (thicknessMeters <= 1e-6) return meltKelvin;
    const conductance = p.crustConductivity / thicknessMeters;
    const background = Math.pow(p.radiativeEquilibriumKelvin, 4);
    let low = p.radiativeEquilibriumKelvin;
    let high = meltKelvin;
    for (let i = 0; i < 48; i++) {
        const ts = 0.5 * (low + high);
        const residual = p.emissivity * SIGMA * (ts ** 4 - background) - conductance * (meltKelvin - ts);
        if (residual > 0) high = ts; else low = ts;
    }
    return 0.5 * (low + high);
}

export function radiogenicWatts(timeSeconds, p = PARAMETERS) {
    const decay = Math.log(p.radiogenicDecayRatio) / (p.presentAgeYears * SECONDS_PER_YEAR);
    return p.presentRadiogenicWatts * Math.exp(decay * (p.presentAgeYears * SECONDS_PER_YEAR - timeSeconds));
}

export function effectiveHeatCapacity(potentialKelvin, p = PARAMETERS) {
    const melting = potentialKelvin < p.liquidusPotentialKelvin && potentialKelvin > p.solidusPotentialKelvin
        ? p.latentHeat / (p.liquidusPotentialKelvin - p.solidusPotentialKelvin) : 0.0;
    return p.mantleMassKilograms * (p.specificHeat + melting);
}

// Turbulent magma-ocean convective velocity (Solomatov 2000 scaling),
// reduced by the crystal-suspension viscosity as the ocean approaches the
// rheological transition.
export function magmaConvectionVelocity(potentialKelvin, heatFlux, p = PARAMETERS) {
    const buoyancyFlux = p.thermalExpansivity * p.gravity * Math.max(heatFlux, 1.0) * p.mantleDepthMeters /
        (p.meltDensity * p.specificHeat);
    const turbulent = 0.6 * Math.cbrt(buoyancyFlux);
    return turbulent / suspensionViscosityRatio(crystalFraction(potentialKelvin, p), p);
}

// Surface statistics for a magma ocean whose quench crust lives for
// min(transit, founder, elapsed). Crust ages are uniformly distributed in that
// window (steady renewal); the mean radiated flux is integrated numerically.
export function magmaSurfaceStatistics(potentialKelvin, cellScaleMeters, previousFlux, elapsedSeconds, p = PARAMETERS) {
    const velocity = magmaConvectionVelocity(potentialKelvin, previousFlux, p);
    const transitSeconds = cellScaleMeters / Math.max(velocity, 1e-9);
    const founderSeconds = (p.foundingThicknessMeters / (2.0 * p.stefanLambda)) ** 2 / p.crustDiffusivity;
    const lifetime = Math.max(1e-3, Math.min(transitSeconds, founderSeconds));
    // Crust renewal phase is uniform in [0, lifetime]; no crust can be older
    // than the planet's elapsed time. Log-spaced quadrature over age.
    const reach = Math.max(1e-3, Math.min(lifetime, elapsedSeconds));
    const samples = 40;
    const start = Math.min(1e-2, reach * 1e-4);
    const surfaceFlux = age => {
        const ts = crustSurfaceKelvin(crustThicknessMeters(age, p), potentialKelvin, p);
        return { ts, q: p.emissivity * SIGMA * (ts ** 4 - p.radiativeEquilibriumKelvin ** 4) };
    };
    let integral = p.emissivity * SIGMA * potentialKelvin ** 4 * start;
    let previousAge = start;
    let previousQ = integral / start;
    let glowingSeconds = 0;
    let lastTs = potentialKelvin;
    for (let i = 1; i <= samples; i++) {
        const age = start * Math.pow(reach / start, i / samples);
        const { ts, q } = surfaceFlux(age);
        integral += 0.5 * (q + previousQ) * (age - previousAge);
        if (ts > 1000.0) glowingSeconds = age;
        previousAge = age;
        previousQ = q;
        lastTs = ts;
    }
    // Crust that would be older than the elapsed time is capped at that age.
    integral += previousQ * (lifetime - reach);
    if (lastTs > 1000.0) glowingSeconds = lifetime;
    return {
        velocity,
        transitSeconds,
        founderSeconds,
        lifetimeSeconds: lifetime,
        meanFlux: integral / lifetime,
        glowingFraction: Math.min(1.0, glowingSeconds / lifetime),
    };
}

export function solidStateHeatFlux(potentialKelvin, plateRegime, p = PARAMETERS) {
    const surface = p.radiativeEquilibriumKelvin;
    const drive = Math.max(potentialKelvin - surface, 0) / (p.presentPotentialKelvin - surface);
    const viscosity = Math.exp(p.activationEnergy / (3.0 * GAS_CONSTANT) *
        (1.0 / p.presentPotentialKelvin - 1.0 / potentialKelvin));
    const regime = plateRegime ? 1.0 : p.stagnantLidEfficiency;
    return p.presentMantleHeatFlux * Math.pow(drive, 4.0 / 3.0) * viscosity * regime;
}

export function heatPipeShare(potentialKelvin, plateRegime, p = PARAMETERS) {
    if (plateRegime) return 0.0;
    const span = p.solidusPotentialKelvin - p.plateOnsetPotentialKelvin;
    return p.heatPipeShareAtSolidus * Math.min(1.0, Math.max(0.15, (potentialKelvin - p.plateOnsetPotentialKelvin) / span));
}

export function createThermalHistory(p = PARAMETERS) {
    return {
        timeSeconds: 0.0,
        potentialKelvin: p.initialPotentialKelvin,
        heatFlux: p.emissivity * SIGMA * p.initialPotentialKelvin ** 4,
        regime: 'magma-ocean',
        plateOnsetSeconds: null,
        lidOnsetSeconds: null,
        magma: null,
        heatPipeShare: 0.0,
    };
}

// Advance by dt seconds; cellScaleMeters is the resolved raft scale.
export function advanceThermalHistory(state, dtSeconds, cellScaleMeters, p = PARAMETERS) {
    const area = surfaceArea(p);
    let remaining = dtSeconds;
    let guard = 0;
    while (remaining > 0 && guard++ < 4000) {
        let flux;
        if (state.regime === 'magma-ocean') {
            state.magma = magmaSurfaceStatistics(state.potentialKelvin, cellScaleMeters, state.heatFlux, state.timeSeconds, p);
            flux = state.magma.meanFlux;
        } else {
            flux = solidStateHeatFlux(state.potentialKelvin, state.regime === 'plates', p);
            state.heatPipeShare = heatPipeShare(state.potentialKelvin, state.regime === 'plates', p);
        }
        state.heatFlux = flux;
        const power = radiogenicWatts(state.timeSeconds, p) - flux * area;
        const capacity = effectiveHeatCapacity(state.potentialKelvin, p);
        const rate = power / capacity;
        // Limit each substep to 0.5 K so regime thresholds are resolved.
        const step = Math.min(remaining, rate !== 0 ? 0.5 / Math.abs(rate) : remaining);
        state.potentialKelvin += rate * step;
        state.timeSeconds += step;
        remaining -= step;
        if (state.regime === 'magma-ocean' && crystalFraction(state.potentialKelvin, p) >= p.rheologicalTransition) {
            state.regime = 'stagnant-lid';
            state.lidOnsetSeconds = state.timeSeconds;
            state.magma = null;
        } else if (state.regime === 'stagnant-lid' && state.potentialKelvin <= p.plateOnsetPotentialKelvin) {
            state.regime = 'plates';
            state.plateOnsetSeconds = state.timeSeconds;
        }
    }
    if (remaining > 0) {
        state.timeSeconds += remaining;
    }
    return state;
}

// Display time mapping: physical time grows exponentially with wall time
// (one decade per `wallSecondsPerDecade`) until the selected cap. This is a
// presentation choice: cooling is front-loaded (T^4 radiative law).
export function physicalRate(timeSeconds, maximumYearsPerSecond, wallSecondsPerDecade = 5.0) {
    const logarithmic = Math.max(timeSeconds, 1.0) * Math.LN10 / wallSecondsPerDecade;
    return Math.min(logarithmic, maximumYearsPerSecond * SECONDS_PER_YEAR);
}

// Integrate the wall-clock budget in bounded increments, independent of GPU
// frame rate. Dropping all but 100 ms of a slow frame makes a fast-forward
// replay arbitrarily slow on software WebGL and mobile GPUs. The thermodynamic
// integrator still owns all temperature, latent heat and regime transitions.
export function advancePlaybackThermalHistory(state, wallDtSeconds, cellScaleMeters,
    maximumYearsPerSecond, maximumRainoutYearsPerSecond = Infinity) {
    if (!Number.isFinite(wallDtSeconds) || wallDtSeconds < 0 ||
        !Number.isFinite(maximumYearsPerSecond) || maximumYearsPerSecond <= 0 ||
        !(cellScaleMeters > 0) || maximumRainoutYearsPerSecond <= 0) {
        throw new RangeError('Playback duration, cell scale and rate limits must be finite, positive where required.');
    }
    // The renderer resets its playback timestamp on visibility changes.
    // Do not discard normal foreground frame time on software/slow WebGL:
    // otherwise the exponential clock cannot escape the magma-ocean phase.
    // Bound unexpected long stalls to avoid skipping whole geological eras.
    let remaining = Math.min(wallDtSeconds, 30.0);
    let rate = 0;
    while (remaining > 1e-9) {
        const step = Math.min(remaining, 0.1);
        rate = Math.min(physicalRate(state.timeSeconds, maximumYearsPerSecond, 3.5),
            maximumRainoutYearsPerSecond * SECONDS_PER_YEAR);
        advanceThermalHistory(state, rate * step, cellScaleMeters);
        remaining -= step;
    }
    return rate;
}

export function formatDuration(seconds) {
    const years = seconds / SECONDS_PER_YEAR;
    if (seconds < 120) return `${seconds.toFixed(0)} s`;
    if (seconds < 7200) return `${(seconds / 60).toFixed(0)} min`;
    if (seconds < 3 * 86400) return `${(seconds / 3600).toFixed(1)} h`;
    if (years < 1) return `${(seconds / 86400).toFixed(0)} days`;
    if (years < 1e3) return `${years.toFixed(years < 10 ? 1 : 0)} yr`;
    if (years < 1e6) return `${(years / 1e3).toFixed(years < 1e4 ? 1 : 0)} kyr`;
    if (years < 1e9) return `${(years / 1e6).toFixed(years < 1e7 ? 1 : 0)} Myr`;
    return `${(years / 1e9).toFixed(2)} Gyr`;
}
