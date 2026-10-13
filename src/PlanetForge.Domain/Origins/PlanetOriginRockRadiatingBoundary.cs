namespace PlanetForge.Domain.Origins;

/// <summary>
/// Massless finite-volume face: k (Tcell - Tface) / halfWidth + absorbedFlux = epsilon sigma Tface^4.
/// This reconstructs a boundary temperature, not a second rock energy reservoir or phase parcel.
/// </summary>
public static class PlanetOriginRockRadiatingBoundary
{
    public static double ResolveTemperature(double cellKelvin, double conductivity, double halfWidthMeters,
        double absorbedWattsPerSquareMeter, double emissivity)
    {
        if (!double.IsFinite(cellKelvin) || cellKelvin < 0.0 || !double.IsFinite(conductivity) || conductivity < 0.0 ||
            !double.IsFinite(halfWidthMeters) || halfWidthMeters <= 0.0 || !double.IsFinite(absorbedWattsPerSquareMeter) ||
            absorbedWattsPerSquareMeter < 0.0 || !double.IsFinite(emissivity) || emissivity < 0.0 || emissivity > 1.0)
            throw new ArgumentOutOfRangeException(nameof(cellKelvin));
        var conductance = conductivity / halfWidthMeters;
        var radiation = emissivity * PlanetRadiationBalanceModel.StefanBoltzmannWattsPerSquareMeterKelvinFourth;
        if (radiation == 0.0)
        {
            if (conductance == 0.0 && absorbedWattsPerSquareMeter > 0.0)
                throw new InvalidOperationException("An insulated nonemitting massless face cannot absorb heat.");
            return conductance == 0.0 ? cellKelvin : cellKelvin + absorbedWattsPerSquareMeter / conductance;
        }
        var equilibrium = Math.Pow(absorbedWattsPerSquareMeter / radiation, 0.25);
        if (conductance == 0.0) return equilibrium;
        if (!double.IsFinite(conductance) || !double.IsFinite(equilibrium))
            throw new InvalidOperationException("Radiating face coefficients overflowed.");
        var low = 0.0;
        var high = Math.Max(cellKelvin, equilibrium);
        // Monotone balance with a guaranteed nonnegative bracket. Fixed iterations ensure deterministic replay.
        for (var i = 0; i < 64; i++)
        {
            var mid = (low + high) * 0.5;
            var residual = conductance * (cellKelvin - mid) + absorbedWattsPerSquareMeter - radiation * Math.Pow(mid, 4);
            if (residual > 0.0) low = mid;
            else high = mid;
        }
        return (low + high) * 0.5;
    }
}
