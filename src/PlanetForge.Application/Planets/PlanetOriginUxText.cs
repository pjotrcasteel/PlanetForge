using System.Globalization;
using PlanetForge.Application.Rendering;

namespace PlanetForge.Application.Planets;

/// <summary>
/// User-facing labels for one published Earth-origin epoch. Labels describe
/// only observed canonical model values; they do not claim rainfall, crust
/// formation or life without their own physically validated process state.
/// </summary>
public static class PlanetOriginUxText
{
    public const string ScenarioLabel = "EARTH ORIGIN · PHYSICAL MODEL";

    public static string GeologicalTime(PlanetOriginSurfaceSnapshot origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        var ageGa = origin.YearsBeforePresent / 1_000_000_000.0;
        return ageGa.ToString("0.000", CultureInfo.InvariantCulture) + " Ga AGO";
    }

    public static string StateLabel(PlanetOriginSurfaceSnapshot origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        var rock = origin.BulkRockMeltFraction switch
        {
            >= 1.0 => "MOLTEN INTERIOR",
            > 0.0 => "PARTLY MOLTEN INTERIOR",
            _ => "SOLID INTERIOR · BULK MODEL"
        };

        // Atmospheric H2O is not evidence of surface rain; surface liquid
        // inventory is not proof of an ocean or validated liquid stability.
        if (origin.SurfaceLiquidMassKilograms > 0.0)
        {
            return rock + " · SURFACE LIQUID PRESENT";
        }

        if (origin.AtmosphericSteamMassKilograms > 0.0)
        {
            return rock + " · STEAM PRESENT";
        }

        return rock + " · NO SURFACE WATER";
    }
}
