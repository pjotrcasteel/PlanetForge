using PlanetForge.Application.Rendering;
using PlanetForge.Application.Surface;
using PlanetForge.Application.Surface.Hydrology;
using PlanetForge.Domain.Atmosphere;
using PlanetForge.Domain.Climate;
using PlanetForge.Domain.Hydrology;
using PlanetForge.Domain.Origins;
using PlanetForge.Domain.Physics;
using PlanetForge.Domain.Planets;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Planets;

public sealed class PlanetExperience(
    PlanetSurfaceMeshCache surfaceMeshCache,
    PlanetLocalSurfacePatchSampler localSurfacePatchSampler,
    PlanetLocalSurfaceMeshBuilder localSurfaceMeshBuilder,
    IPlanetTerrainDeformationStore? terrainDeformationStore = null,
    PlanetSurfaceLodSelector? surfaceLodSelector = null,
    PlanetHypsometryCache? hypsometryCache = null)
{
    private const int GlobalSurfaceLevel = 1;
    private const int SurfaceCellsPerAxis = 32;
    private const int AdaptiveSurfaceCellsPerAxis = 20;
    private const double OrbitalRefinementStartDistance = 1.35;
    private const int CoarseLocalSurfaceCellsPerAxis = 16;
    private const int MediumLocalSurfaceCellsPerAxis = 16;
    private const int DetailedLocalSurfaceCellsPerAxis = 24;
    private const int FineLocalSurfaceCellsPerAxis = 32;
    private const double MinimumLocalPatchSizeMeters = 16.0;
    private const double MaximumLocalViewAltitudeMeters = 20_000.0;
    private const double MediumLocalResolutionAltitudeMeters = 3_000.0;
    private const double DetailedLocalResolutionAltitudeMeters = 800.0;
    private const double FineLocalResolutionAltitudeMeters = 60.0;
    private const double MaximumLocalPatchRadiusFraction = 0.18;
    private const double LocalPatchMarginFactor = 2.2;
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
    // One low-cost area-weighted globe sample (6 * 8 * 8 cells), cached by
    // geological revision. Refine only after measuring WASM/iPhone startup cost.
    private const int SeaLevelSamplingGridLevel = 3;
    private const double LiquidWaterDensityKilogramsPerCubicMeter = 1_000.0;
    private const double MinimumVisualSeaLevelMeters = -11_000.0;
    private const double MaximumVisualSeaLevelMeters = 6_000.0;

    private readonly PlanetState state = new();
    private readonly PlanetSurfaceLodSelector lodSelector = surfaceLodSelector ?? new PlanetSurfaceLodSelector(PlanetSurfaceLodOptions.Default);
    private PlanetSurfaceView? surfaceView;
    private PlanetVector? localAnchorDirection;
    private LocalSurfaceCacheKey? cachedLocalSurfaceKey;
    private PlanetLocalSurfaceMesh? cachedLocalSurface;
    private ClimateFeedbackState? climateState;
    private PlanetOriginEpochState? publishedEarthOrigin;

    /// <summary>
    /// Opt in one conserved Earth-origin epoch on the existing physical planet.
    /// The currently playable climate experiment is untouched until enabled.
    /// </summary>
    public PlanetRenderSnapshot PublishEarthOrigin(PlanetOriginEpochState epoch)
    {
        ArgumentNullException.ThrowIfNull(epoch);
        if (epoch.CoupledEnergy is null)
        {
            throw new InvalidOperationException("The main planet requires a coupled Earth-origin energy ledger.");
        }

        if (publishedEarthOrigin is not null && (epoch.Revision <= publishedEarthOrigin.Revision ||
            epoch.Clock.YearsSinceSolarSystemFormation < publishedEarthOrigin.Clock.YearsSinceSolarSystemFormation ||
            (epoch.Clock.YearsSinceSolarSystemFormation == publishedEarthOrigin.Clock.YearsSinceSolarSystemFormation &&
                epoch.Clock.MicrosecondsIntoYear <= publishedEarthOrigin.Clock.MicrosecondsIntoYear)))
        {
            throw new InvalidOperationException("The main planet only accepts a newer geological epoch.");
        }

        var previous = publishedEarthOrigin;
        publishedEarthOrigin = epoch;
        try
        {
            return CreateSnapshot();
        }
        catch
        {
            publishedEarthOrigin = previous;
            throw;
        }
    }

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
        RequireLegacyClimateMode();
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
        RequireLegacyClimateMode();
        var parameters = state.PhysicalParameters;
        state.SetPhysicalParameters(parameters with { BondAlbedo = Math.Clamp(parameters.BondAlbedo - 0.03, MinimumBondAlbedo, MaximumBondAlbedo) });
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot BrightenSurface()
    {
        RequireLegacyClimateMode();
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
        SetSeed(unchecked((state.Seed * 397) ^ 7919));
        return CreateInitialRenderSnapshot();
    }

    public void SetSeed(int seed)
    {
        state.Reseed(seed);
        publishedEarthOrigin = null;
        surfaceView = null;
        localAnchorDirection = null;
        cachedLocalSurfaceKey = null;
        cachedLocalSurface = null;
        surfaceMeshCache.Clear();
    }

    public PlanetRenderSnapshot ResetEarthReference()
    {
        state.ResetEarthReference();
        publishedEarthOrigin = null;
        climateState = null;
        return CreateSnapshot();
    }

    public PlanetExperienceState ExportState()
    {
        RequireLegacyClimateMode();
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
        publishedEarthOrigin = null;
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
        var seaLevelMeters = CalculateSeaLevelMeters(climateResult.Water);
        var atmosphereDensity = CalculateAtmosphereDensity(atmosphere.SurfacePressurePascals);
        var radiusMeters = state.PhysicalParameters.RadiusMeters;
        var localSurface = CreateLocalSurface(radiusMeters);
        var surfaceTiles = localSurface is null
            ? CreateGlobalSurfaceTiles(includeFullGlobalGeometry, radiusMeters)
            : Array.Empty<PlanetSurfaceTileMesh>();

        var deformationRevision = terrainDeformationStore?.GetRevision(state.Seed) ?? 0;
        var geologyRevision = localSurfacePatchSampler.ElevationSource is IPlanetElevationRevisionSource source ? source.Revision : 0;
        // Existing gameplay revision identities stay unchanged until opt-in
        // regional geology is published. A new atlas forces GPU water and rock
        // overlays to observe a different physical terrain generation.
        var terrainRevision = geologyRevision == 0 ? deformationRevision : HashCode.Combine(deformationRevision, geologyRevision);
        var rendered = new PlanetRenderSnapshot(
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
            terrainRevision);

        return publishedEarthOrigin is null ? rendered : ProjectEarthOrigin(rendered, publishedEarthOrigin);
    }

    private PlanetRenderSnapshot ProjectEarthOrigin(PlanetRenderSnapshot rendered, PlanetOriginEpochState epoch)
    {
        var liquid = epoch.Water.GetMassKilograms(PlanetWaterReservoir.SurfaceLiquid);
        var ice = epoch.Water.GetMassKilograms(PlanetWaterReservoir.SurfaceIce);
        var vapor = epoch.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor);
        var accessibleWater = liquid + ice + vapor;
        if (!double.IsFinite(accessibleWater))
        {
            throw new InvalidOperationException("The surface water projection requires finite H2O mass.");
        }

        var water = new WaterPhaseSnapshot(
            ice, liquid, vapor,
            accessibleWater > 0.0 ? ice / accessibleWater : 0.0,
            accessibleWater > 0.0 ? liquid / accessibleWater : 0.0,
            accessibleWater > 0.0 ? vapor / accessibleWater : 0.0,
            0.0, // Explicit unknown: Earth-origin phase stability never uses Snowball's boiling point.
            false); // Stability needs a pressure/EOS/phase proof, not the old Snowball model.
        var temperatures = epoch.Temperatures!.Value;
        var origin = new PlanetOriginRenderState(
            epoch.Revision, epoch.Clock.YearsBeforePresent,
            epoch.Rock.TemperatureKelvin, epoch.Rock.MeltFraction,
            temperatures.AtmosphereTemperatureKelvin, temperatures.SurfaceRockTemperatureKelvin,
            vapor, liquid, ice);

        return rendered with
        {
            Water = water,
            SeaLevelMeters = CalculateSeaLevelMeters(water),
            Origin = origin
        };
    }

    private IReadOnlyList<PlanetSurfaceTileMesh> CreateGlobalSurfaceTiles(bool fullGeometry, double radiusMeters)
    {
        if (surfaceView is null || surfaceView.CameraDistanceFromCenter >= OrbitalRefinementStartDistance)
        {
            return fullGeometry
                ? surfaceMeshCache.GetOrBuildGlobalFull(GlobalSurfaceLevel, SurfaceCellsPerAxis, state.Seed, radiusMeters)
                : surfaceMeshCache.GetOrBuildGlobal(GlobalSurfaceLevel, SurfaceCellsPerAxis, state.Seed, radiusMeters);
        }

        // Use the existing view-dependent cube-sphere selector rather than repeatedly
        // magnifying the same 24 level-one tiles until the local-ground transition.
        var selectedTiles = lodSelector.Select(surfaceView);
        return surfaceMeshCache.GetOrBuild(selectedTiles, AdaptiveSurfaceCellsPerAxis, state.Seed, radiusMeters);
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
        var revision = localSurfacePatchSampler.ElevationSource is IPlanetElevationRevisionSource source ? source.Revision : 0;
        var cacheKey = new LocalSurfaceCacheKey(anchorAddress, patchSizeMeters, cellsPerAxis, state.Seed, radiusMeters, revision);

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

    private void RequireLegacyClimateMode()
    {
        if (publishedEarthOrigin is not null)
        {
            throw new InvalidOperationException(
                "Earth-origin changes must enter through the conserved geological epoch, not legacy climate controls.");
        }
    }

    private void ChangeOrbitalDistance(double deltaAstronomicalUnits)
    {
        RequireLegacyClimateMode();
        var parameters = state.PhysicalParameters;
        var current = parameters.OrbitalDistanceMeters / PhysicalConstants.AstronomicalUnitMeters;
        var next = Math.Clamp(current + deltaAstronomicalUnits, MinimumOrbitalDistanceAu, MaximumOrbitalDistanceAu);
        state.SetPhysicalParameters(parameters with { OrbitalDistanceMeters = next * PhysicalConstants.AstronomicalUnitMeters });
    }

    private void ChangeStellarLuminosity(double deltaSolarLuminosity)
    {
        RequireLegacyClimateMode();
        var parameters = state.PhysicalParameters;
        var current = parameters.StellarLuminosityWatts / PhysicalConstants.NominalSolarLuminosityWatts;
        var next = Math.Clamp(current + deltaSolarLuminosity, MinimumStellarLuminositySolar, MaximumStellarLuminositySolar);
        state.SetPhysicalParameters(parameters with { StellarLuminosityWatts = next * PhysicalConstants.NominalSolarLuminosityWatts });
    }

    private void ChangeMass(double deltaEarthMass)
    {
        RequireLegacyClimateMode();
        var parameters = state.PhysicalParameters;
        var current = parameters.MassKilograms / EarthReference.MassKilograms;
        var next = Math.Clamp(current + deltaEarthMass, MinimumPlanetMassEarth, MaximumPlanetMassEarth);
        state.SetPhysicalParameters(parameters with { MassKilograms = next * EarthReference.MassKilograms });
    }

    private void ChangeRadius(double deltaEarthRadius)
    {
        RequireLegacyClimateMode();
        var parameters = state.PhysicalParameters;
        var current = parameters.RadiusMeters / EarthReference.MeanRadiusMeters;
        var next = Math.Clamp(current + deltaEarthRadius, MinimumPlanetRadiusEarth, MaximumPlanetRadiusEarth);
        state.SetPhysicalParameters(parameters with { RadiusMeters = next * EarthReference.MeanRadiusMeters });
    }

    private void ChangeAtmosphereMass(double deltaEarthAtmospheres)
    {
        RequireLegacyClimateMode();
        var parameters = state.AtmosphereParameters;
        var current = parameters.MassKilograms / EarthAtmosphereReference.TotalMassKilograms;
        var next = Math.Clamp(current + deltaEarthAtmospheres, MinimumAtmosphereEarthMasses, MaximumAtmosphereEarthMasses);
        state.SetAtmosphereParameters(parameters with { MassKilograms = next * EarthAtmosphereReference.TotalMassKilograms });
    }

    private void ChangeCarbonDioxide(double factor)
    {
        RequireLegacyClimateMode();
        var parameters = state.AtmosphereParameters;
        var next = Math.Clamp(parameters.CarbonDioxidePartsPerMillion * factor, MinimumCarbonDioxidePartsPerMillion, MaximumCarbonDioxidePartsPerMillion);
        state.SetAtmosphereParameters(parameters with { CarbonDioxidePartsPerMillion = next });
    }

    private void ChangeWater(double deltaEarthHydrospheres)
    {
        RequireLegacyClimateMode();
        var parameters = state.WaterParameters;
        var current = parameters.TotalMassKilograms / EarthWaterReference.TotalHydrosphereMassKilograms;
        var next = Math.Clamp(current + deltaEarthHydrospheres, MinimumWaterEarthHydrospheres, MaximumWaterEarthHydrospheres);
        state.SetWaterParameters(parameters with { TotalMassKilograms = next * EarthWaterReference.TotalHydrosphereMassKilograms });
    }

    private double CalculateSeaLevelMeters(WaterPhaseSnapshot water)
    {
        // Frozen/dry worlds should not sample the full globe at startup.
        if (water.LiquidMassKilograms <= 0.0)
        {
            return MinimumVisualSeaLevelMeters;
        }

        // Tests and legacy manually constructed experiences can retain their
        // previous behavior until opting into the shared physical model.
        if (hypsometryCache is null)
        {
            return CalculateVisualSeaLevelMeters(water);
        }

        var planetRadiusMeters = state.PhysicalParameters.RadiusMeters;
        var terrainVolume = hypsometryCache.GetOrBuild(
            SeaLevelSamplingGridLevel, state.Seed, planetRadiusMeters, CancellationToken.None);
        var liquidVolumeCubicMeters = water.LiquidMassKilograms / LiquidWaterDensityKilogramsPerCubicMeter;
        return terrainVolume.SolveSeaLevelMeters(liquidVolumeCubicMeters);
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
        double PlanetRadiusMeters,
        long GeologyRevision);
}