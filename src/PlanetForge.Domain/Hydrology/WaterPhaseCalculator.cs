namespace PlanetForge.Domain.Hydrology;

public static class WaterPhaseCalculator
{
    private const double TriplePointPressurePascals = 611.657;
    private const double TriplePointTemperatureKelvin = 273.16;
    private const double ReferenceBoilingPointKelvin = 373.15;
    private const double ReferencePressurePascals = 101_325.0;
    private const double WaterVaporGasConstantJoulesPerKilogramKelvin = 461.5;
    private const double LatentHeatVaporizationJoulesPerKilogram = 2.257e6;
    private const double MaximumBoilingPointKelvin = 647.096;

    public static WaterPhaseSnapshot Calculate(WaterParameters water, double surfaceTemperatureKelvin, double surfacePressurePascals)
    {
        ArgumentNullException.ThrowIfNull(water);
        Validate(water, surfaceTemperatureKelvin, surfacePressurePascals);

        if (water.TotalMassKilograms == 0.0)
        {
            return new WaterPhaseSnapshot(0.0, 0.0, 0.0, 0.0, 0.0, 0.0, CalculateBoilingPoint(surfacePressurePascals), false);
        }

        if (surfacePressurePascals < TriplePointPressurePascals)
        {
            var sublimatedFraction = SmoothStep(258.0, 278.0, surfaceTemperatureKelvin);
            return CreateSnapshot(water.TotalMassKilograms, 1.0 - sublimatedFraction, 0.0, sublimatedFraction, TriplePointTemperatureKelvin, false);
        }

        var boilingPoint = CalculateBoilingPoint(surfacePressurePascals);
        var liquidRange = Math.Max(boilingPoint - TriplePointTemperatureKelvin, 0.2);
        var transitionWidth = Math.Min(5.0, Math.Max(0.05, liquidRange / 4.0));
        double iceFraction;
        double liquidFraction;
        double vaporFraction;

        if (surfaceTemperatureKelvin <= TriplePointTemperatureKelvin - transitionWidth)
        {
            iceFraction = 1.0;
            liquidFraction = 0.0;
            vaporFraction = 0.0;
        }
        else if (surfaceTemperatureKelvin < TriplePointTemperatureKelvin + transitionWidth)
        {
            liquidFraction = SmoothStep(
                TriplePointTemperatureKelvin - transitionWidth,
                TriplePointTemperatureKelvin + transitionWidth,
                surfaceTemperatureKelvin);
            iceFraction = 1.0 - liquidFraction;
            vaporFraction = 0.0;
        }
        else if (surfaceTemperatureKelvin <= boilingPoint - transitionWidth)
        {
            iceFraction = 0.0;
            liquidFraction = 1.0;
            vaporFraction = 0.0;
        }
        else if (surfaceTemperatureKelvin < boilingPoint + transitionWidth)
        {
            vaporFraction = SmoothStep(boilingPoint - transitionWidth, boilingPoint + transitionWidth, surfaceTemperatureKelvin);
            iceFraction = 0.0;
            liquidFraction = 1.0 - vaporFraction;
        }
        else
        {
            iceFraction = 0.0;
            liquidFraction = 0.0;
            vaporFraction = 1.0;
        }

        return CreateSnapshot(water.TotalMassKilograms, iceFraction, liquidFraction, vaporFraction, boilingPoint, true);
    }

    private static WaterPhaseSnapshot CreateSnapshot(
        double totalMassKilograms,
        double iceFraction,
        double liquidFraction,
        double vaporFraction,
        double boilingPointKelvin,
        bool liquidWaterStable)
    {
        return new WaterPhaseSnapshot(
            totalMassKilograms * iceFraction,
            totalMassKilograms * liquidFraction,
            totalMassKilograms * vaporFraction,
            iceFraction,
            liquidFraction,
            vaporFraction,
            boilingPointKelvin,
            liquidWaterStable);
    }

    private static double CalculateBoilingPoint(double pressurePascals)
    {
        if (pressurePascals <= TriplePointPressurePascals)
        {
            return TriplePointTemperatureKelvin;
        }

        var reciprocalTemperature = 1.0 / ReferenceBoilingPointKelvin
            - WaterVaporGasConstantJoulesPerKilogramKelvin / LatentHeatVaporizationJoulesPerKilogram
                * Math.Log(pressurePascals / ReferencePressurePascals);

        return Math.Clamp(1.0 / reciprocalTemperature, TriplePointTemperatureKelvin, MaximumBoilingPointKelvin);
    }

    private static double SmoothStep(double edge0, double edge1, double value)
    {
        var t = Math.Clamp((value - edge0) / (edge1 - edge0), 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
    }

    private static void Validate(WaterParameters water, double surfaceTemperatureKelvin, double surfacePressurePascals)
    {
        if (!double.IsFinite(water.TotalMassKilograms) || water.TotalMassKilograms < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(water.TotalMassKilograms), water.TotalMassKilograms, "Water mass must be finite and non-negative.");
        }

        if (!double.IsFinite(surfaceTemperatureKelvin) || surfaceTemperatureKelvin <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(surfaceTemperatureKelvin), surfaceTemperatureKelvin, "Temperature must be finite and greater than zero.");
        }

        if (!double.IsFinite(surfacePressurePascals) || surfacePressurePascals < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(surfacePressurePascals), surfacePressurePascals, "Pressure must be finite and non-negative.");
        }
    }
}
