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
        var temperature = high;
        var scale = Math.Max(1.0, conductance * cellKelvin + absorbedWattsPerSquareMeter);
        // Safeguarded Newton on a monotone, bracketed face balance. Deterministic bounded iterations.
        for (var i = 0; i < 64; i++)
        {
            var squared = temperature * temperature;
            var residual = conductance * (cellKelvin - temperature) + absorbedWattsPerSquareMeter - radiation * squared * squared;
            if (Math.Abs(residual) <= scale * 1e-14) return temperature;
            if (residual > 0.0) low = temperature;
            else high = temperature;
            var proposal = temperature + residual / (conductance + 4.0 * radiation * squared * temperature);
            temperature = double.IsFinite(proposal) && proposal > low && proposal < high ? proposal : (low + high) * 0.5;
        }
        return (low + high) * 0.5;
    }
}
