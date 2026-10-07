using PlanetForge.Application.Rendering;
using PlanetForge.Application.Surface;
using PlanetForge.Domain.Atmosphere;
using PlanetForge.Domain.Climate;
using PlanetForge.Domain.Hydrology;
using PlanetForge.Domain.Physics;
using PlanetForge.Domain.Planets;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Planets;

public sealed class PlanetExperience(
    PlanetSurfaceMeshCache surfaceMeshCache,
    PlanetLocalSurfacePatchSampler localSurfacePatchSampler,
    PlanetLocalSurfaceMeshBuilder localSurfaceMeshBuilder,
    IPlanetTerrainDeformationStore? terrainDeformationStore = null)
{
    private const int GlobalSurfaceLevel = 1;
    private const int SurfaceCellsPerAxis = 32;
    private const int CoarseLocalSurfaceCellsPerAxis = 8;
    private const int MediumLocalSurfaceCellsPerAxis = 16;
    private const int DetailedLocalSurfaceCellsPerAxis = 24;
    private const int FineLocalSurfaceCellsPerAxis = 32;
    private const double MinimumLocalPatchSizeMeters = 16.0;
    private const double MaximumLocalViewAltitudeMeters = 20_000.0;
    private const double MediumLocalResolutionAltitudeMeters = 3_000.0;
    private const double DetailedLocalResolutionAltitudeMeters = 800.0;
    private const double FineLocalResolutionAltitudeMeters = 60.0;
    private const double MaximumLocalPatchRadiusFraction = 0.18;
    private const double LocalPatchMarginFactor = 1.5;
    private const double MinimumOrbitalDistanceAu = 0.25;
    private const double MaximumOrbitalDistanceAu = 3.0;
    private const double MinimumStellarLuminositySolar = 0.2;
    private const double MaximumStellarLuminositySolar = 2.5;
    private const double MinimumPlanetMassEarth = 0.2;
    private const double MaximumPlanetMassEarth = 5.0;
    private const double MinimumPlanetRadiusEarth = 0.4;
    private const double MaximumPlanetRadiusEarth = 2.5;
    private const double MinimumBondAlbedo = 0.02;
    private const double MaximumBondAlbedo = 0.85;
    private const double MinimumAtmosphereEarthMasses = 0.0;
    private const double MaximumAtmosphereEarthMasses = 5.0;
    private const double MinimumCarbonDioxidePartsPerMillion = 10.0;
    private const double MaximumCarbonDioxidePartsPerMillion = 5_000.0;
    private const double MinimumWaterEarthHydrospheres = 0.0;
    private const double MaximumWaterEarthHydrospheres = 5.0;
    private const double MinimumVisualSeaLevelMeters = -11_000.0;
    private const double MaximumVisualSeaLevelMeters = 6_000.0;

    private readonly PlanetState state = new();
    private PlanetSurfaceView? surfaceView;
    private PlanetVector? localAnchorDirection;
    private LocalSurfaceCacheKey? cachedLocalSurfaceKey;
    private PlanetLocalSurfaceMesh? cachedLocalSurface;
    private ClimateFeedbackState? climateState;

    public PlanetRenderSnapshot CreateSnapshot() => CreateSnapshot(includeFullGlobalGeometry: false);

    public PlanetRenderSnapshot CreateInitialRenderSnapshot() => CreateSnapshot(includeFullGlobalGeometry: true);

    public PlanetRenderSnapshot UpdateSurfaceView(PlanetSurfaceView view)
    {
        var radiusMeters = state.PhysicalParameters.RadiusMeters;
        if (IsLocalView(view, radiusMeters))
        {
            localAnchorDirection ??= PlanetVector.Normalize(view.CameraDirection);
            surfaceView = view with { CameraDirection = localAnchorDirection.Value };
        }
        else if (localAnchorDirection is not null)
        {
            surfaceView = view with { CameraDirection = localAnchorDirection.Value };
            localAnchorDirection = null;
        }
        else
        {
            surfaceView = view;
        }

        return CreateSnapshot();
    }

    public PlanetRenderSnapshot MoveLocalSurfaceAnchor(double eastMeters, double northMeters)
    {
        if (surfaceView is null || localAnchorDirection is null)
        {
            throw new InvalidOperationException("Local surface travel requires an active local surface view.");
        }

        var radiusMeters = state.PhysicalParameters.RadiusMeters;
        localAnchorDirection = PlanetSurfaceNavigator.Move(localAnchorDirection.Value, eastMeters, northMeters, radiusMeters);
        surfaceView = surfaceView with { CameraDirection = localAnchorDirection.Value };
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot InvalidateTerrain()
    {
        surfaceMeshCache.Clear();
        cachedLocalSurfaceKey = null;
        cachedLocalSurface = null;
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot AdvanceClimate(double years)
    {
        var basePhysics = PlanetPhysicsCalculator.Calculate(state.PhysicalParameters);
        var atmosphere = AtmosphereCalculator.Calculate(state.AtmosphereParameters, state.PhysicalParameters, basePhysics);
        climateState ??= ClimateFeedbackSimulator.Initialize(state.PhysicalParameters, atmosphere, state.WaterParameters);
        climateState = ClimateFeedbackSimulator.Advance(state.PhysicalParameters, atmosphere, state.WaterParameters, climateState, years);
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot MoveOrbitInward()
    {
        ChangeOrbitalDistance(-0.1);
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot MoveOrbitOutward()
    {
        ChangeOrbitalDistance(0.1);
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot DimStar()
    {
        ChangeStellarLuminosity(-0.1);
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot BrightenStar()
    {
        ChangeStellarLuminosity(0.1);
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot DarkenSurface()
    {
        var parameters = state.PhysicalParameters;
        state.SetPhysicalParameters(parameters with { BondAlbedo = Math.Clamp(parameters.BondAlbedo - 0.03, MinimumBondAlbedo, MaximumBondAlbedo) });
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot BrightenSurface()
    {
        var parameters = state.PhysicalParameters;
        state.SetPhysicalParameters(parameters with { BondAlbedo = Math.Clamp(parameters.BondAlbedo + 0.03, MinimumBondAlbedo, MaximumBondAlbedo) });
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot DecreaseMass()
    {
        ChangeMass(-0.1);
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot IncreaseMass()
    {
        ChangeMass(0.1);
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot DecreaseRadius()
    {
        ChangeRadius(-0.05);
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot IncreaseRadius()
    {
        ChangeRadius(0.05);
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot DecreaseAtmosphereMass()
    {
        ChangeAtmosphereMass(-0.25);
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot IncreaseAtmosphereMass()
    {
        ChangeAtmosphereMass(0.25);
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot HalveCarbonDioxide()
    {
        ChangeCarbonDioxide(0.5);
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot DoubleCarbonDioxide()
    {
        ChangeCarbonDioxide(2.0);
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot DecreaseWater()
    {
        ChangeWater(-0.25);
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot IncreaseWater()
    {
        ChangeWater(0.25);
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot Reseed()
    {
        state.Reseed(unchecked((state.Seed * 397) ^ 7919));
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot ResetEarthReference()
    {
        state.ResetEarthReference();
        climateState = null;
        return CreateSnapshot();
    }

    public PlanetExperienceState ExportState()
    {
        _ = CreateSnapshot();
        return new PlanetExperienceState(
            state.Seed,
            state.PhysicalParameters,
            state.AtmosphereParameters,
            state.WaterParameters,
            climateState!);
    }

    public PlanetRenderSnapshot RestoreState(PlanetExperienceState restoredState)
    {
        ArgumentNullException.ThrowIfNull(restoredState);
        state.Reseed(restoredState.Seed);
        state.SetPhysicalParameters(restoredState.PhysicalParameters);
        state.SetAtmosphereParameters(restoredState.AtmosphereParameters);
        state.SetWaterParameters(restoredState.WaterParameters);
        climateState = restoredState.ClimateState;
        surfaceView = null;
        localAnchorDirection = null;
        cachedLocalSurfaceKey = null;
        cachedLocalSurface = null;
        surfaceMeshCache.Clear();
        return CreateInitialRenderSnapshot();
    }

    private PlanetRenderSnapshot CreateSnapshot(bool includeFullGlobalGeometry)
    {
        var basePhysics = PlanetPhysicsCalculator.Calculate(state.PhysicalParameters);
        var atmosphere = AtmosphereCalculator.Calculate(state.AtmosphereParameters, state.PhysicalParameters, basePhysics);
        climateState ??= ClimateFeedbackSimulator.Initialize(state.PhysicalParameters, atmosphere, state.WaterParameters);
        var climateResult = ClimateFeedbackSimulator.Evaluate(state.PhysicalParameters, atmosphere, state.WaterParameters, climateState);
        climateState = climateResult.State;
        var seaLevelMeters = CalculateVisualSeaLevelMeters(climateResult.Water);
        var atmosphereDensity = CalculateAtmosphereDensity(atmosphere.SurfacePressurePascals);
        var radiusMeters = state.PhysicalParameters.RadiusMeters;
        var localSurface = CreateLocalSurface(radiusMeters);
        var surfaceTiles = localSurface is not null
            ? Array.Empty<PlanetSurfaceTileMesh>()
            : includeFullGlobalGeometry
                ? surfaceMeshCache.GetOrBuildGlobalFull(GlobalSurfaceLevel, SurfaceCellsPerAxis, state.Seed, radiusMeters)
                : surfaceMeshCache.GetOrBuildGlobal(GlobalSurfaceLevel, SurfaceCellsPerAxis, state.Seed, radiusMeters);

        return new PlanetRenderSnapshot(
            surfaceTiles,
            seaLevelMeters,
            atmosphereDensity,
            state.Seed,
            state.PhysicalParameters,
            climateResult.Physics,
            state.AtmosphereParameters,
            atmosphere,
            climateResult.Climate,
            climateResult.Feedback,
            state.WaterParameters,
            climateResult.Water,
            localSurface,
            terrainDeformationStore?.GetRevision(state.Seed) ?? 0);
    }

    private PlanetLocalSurfaceMesh? CreateLocalSurface(double radiusMeters)
    {
        if (surfaceView is null || !IsLocalView(surfaceView, radiusMeters))
        {
            return null;
        }

        var cameraAltitudeMeters = Math.Max((surfaceView.CameraDistanceFromCenter - 1.0) * radiusMeters, 1.0);
        var patchSizeMeters = CalculateLocalPatchSize(surfaceView, cameraAltitudeMeters, radiusMeters);
        var cellsPerAxis = CalculateLocalCellsPerAxis(cameraAltitudeMeters);
        var anchorDirection = localAnchorDirection ?? surfaceView.CameraDirection;
        var anchorAddress = PlanetSurfaceAddressing.Encode(anchorDirection);
        var cacheKey = new LocalSurfaceCacheKey(anchorAddress, patchSizeMeters, cellsPerAxis, state.Seed, radiusMeters);

        if (cachedLocalSurfaceKey == cacheKey && cachedLocalSurface is not null)
        {
            return cachedLocalSurface.AsReference(cameraAltitudeMeters);
        }

        var patch = localSurfacePatchSampler.Sample(anchorDirection, patchSizeMeters, cellsPerAxis, state.Seed, radiusMeters, CancellationToken.None);
        cachedLocalSurface = localSurfaceMeshBuilder.Build(patch, cameraAltitudeMeters);
        cachedLocalSurfaceKey = cacheKey;
        return cachedLocalSurface;
    }

    private static bool IsLocalView(PlanetSurfaceView view, double radiusMeters)
    {
        var cameraAltitudeMeters = (view.CameraDistanceFromCenter - 1.0) * radiusMeters;
        return cameraAltitudeMeters <= MaximumLocalViewAltitudeMeters;
    }

    private static int CalculateLocalCellsPerAxis(double cameraAltitudeMeters)
    {
        if (cameraAltitudeMeters <= FineLocalResolutionAltitudeMeters)
        {
            return FineLocalSurfaceCellsPerAxis;
        }

        if (cameraAltitudeMeters <= DetailedLocalResolutionAltitudeMeters)
        {
            return DetailedLocalSurfaceCellsPerAxis;
        }

        if (cameraAltitudeMeters <= MediumLocalResolutionAltitudeMeters)
        {
            return MediumLocalSurfaceCellsPerAxis;
        }

        return CoarseLocalSurfaceCellsPerAxis;
    }

    private static double CalculateLocalPatchSize(PlanetSurfaceView view, double cameraAltitudeMeters, double radiusMeters)
    {
        var visibleHeightMeters = 2.0 * cameraAltitudeMeters * Math.Tan(view.VerticalFieldOfViewRadians * 0.5);
        var visibleWidthMeters = visibleHeightMeters * Math.Max(view.ViewportAspectRatio, 1.0);
        var requiredSizeMeters = Math.Max(MinimumLocalPatchSizeMeters, Math.Max(visibleHeightMeters, visibleWidthMeters) * LocalPatchMarginFactor);
        var maximumSizeMeters = radiusMeters * MaximumLocalPatchRadiusFraction;
        var patchSizeMeters = MinimumLocalPatchSizeMeters;

        while (patchSizeMeters < requiredSizeMeters && patchSizeMeters < maximumSizeMeters)
        {
            patchSizeMeters *= 2.0;
        }

        return Math.Min(patchSizeMeters, maximumSizeMeters);
    }

    private void ChangeOrbitalDistance(double deltaAstronomicalUnits)
    {
        var parameters = state.PhysicalParameters;
        var current = parameters.OrbitalDistanceMeters / PhysicalConstants.AstronomicalUnitMeters;
        var next = Math.Clamp(current + deltaAstronomicalUnits, MinimumOrbitalDistanceAu, MaximumOrbitalDistanceAu);
        state.SetPhysicalParameters(parameters with { OrbitalDistanceMeters = next * PhysicalConstants.AstronomicalUnitMeters });
    }

    private void ChangeStellarLuminosity(double deltaSolarLuminosity)
    {
        var parameters = state.PhysicalParameters;
        var current = parameters.StellarLuminosityWatts / PhysicalConstants.NominalSolarLuminosityWatts;
        var next = Math.Clamp(current + deltaSolarLuminosity, MinimumStellarLuminositySolar, MaximumStellarLuminositySolar);
        state.SetPhysicalParameters(parameters with { StellarLuminosityWatts = next * PhysicalConstants.NominalSolarLuminosityWatts });
    }

    private void ChangeMass(double deltaEarthMass)
    {
        var parameters = state.PhysicalParameters;
        var current = parameters.MassKilograms / EarthReference.MassKilograms;
        var next = Math.Clamp(current + deltaEarthMass, MinimumPlanetMassEarth, MaximumPlanetMassEarth);
        state.SetPhysicalParameters(parameters with { MassKilograms = next * EarthReference.MassKilograms });
    }

    private void ChangeRadius(double deltaEarthRadius)
    {
        var parameters = state.PhysicalParameters;
        var current = parameters.RadiusMeters / EarthReference.MeanRadiusMeters;
        var next = Math.Clamp(current + deltaEarthRadius, MinimumPlanetRadiusEarth, MaximumPlanetRadiusEarth);
        state.SetPhysicalParameters(parameters with { RadiusMeters = next * EarthReference.MeanRadiusMeters });
    }

    private void ChangeAtmosphereMass(double deltaEarthAtmospheres)
    {
        var parameters = state.AtmosphereParameters;
        var current = parameters.MassKilograms / EarthAtmosphereReference.TotalMassKilograms;
        var next = Math.Clamp(current + deltaEarthAtmospheres, MinimumAtmosphereEarthMasses, MaximumAtmosphereEarthMasses);
        state.SetAtmosphereParameters(parameters with { MassKilograms = next * EarthAtmosphereReference.TotalMassKilograms });
    }

    private void ChangeCarbonDioxide(double factor)
    {
        var parameters = state.AtmosphereParameters;
        var next = Math.Clamp(parameters.CarbonDioxidePartsPerMillion * factor, MinimumCarbonDioxidePartsPerMillion, MaximumCarbonDioxidePartsPerMillion);
        state.SetAtmosphereParameters(parameters with { CarbonDioxidePartsPerMillion = next });
    }

    private void ChangeWater(double deltaEarthHydrospheres)
    {
        var parameters = state.WaterParameters;
        var current = parameters.TotalMassKilograms / EarthWaterReference.TotalHydrosphereMassKilograms;
        var next = Math.Clamp(current + deltaEarthHydrospheres, MinimumWaterEarthHydrospheres, MaximumWaterEarthHydrospheres);
        state.SetWaterParameters(parameters with { TotalMassKilograms = next * EarthWaterReference.TotalHydrosphereMassKilograms });
    }

    private static double CalculateVisualSeaLevelMeters(WaterPhaseSnapshot water)
    {
        var liquidEarthHydrospheres = water.LiquidMassKilograms / EarthWaterReference.TotalHydrosphereMassKilograms;
        if (liquidEarthHydrospheres <= 0.0)
        {
            return MinimumVisualSeaLevelMeters;
        }

        var seaLevelMeters = (Math.Sqrt(liquidEarthHydrospheres) - 1.0) * 4_000.0;
        return Math.Clamp(seaLevelMeters, MinimumVisualSeaLevelMeters, MaximumVisualSeaLevelMeters);
    }

    private static double CalculateAtmosphereDensity(double surfacePressurePascals)
    {
        var pressureRatio = surfacePressurePascals / 101_325.0;
        return Math.Clamp(0.62 * Math.Sqrt(Math.Max(pressureRatio, 0.0)), 0.0, 1.0);
    }

    private readonly record struct LocalSurfaceCacheKey(
        PlanetSurfaceAddress AnchorAddress,
        double PatchSizeMeters,
        int CellsPerAxis,
        int Seed,
        double PlanetRadiusMeters);
}