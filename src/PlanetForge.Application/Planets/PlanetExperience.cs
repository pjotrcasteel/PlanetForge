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
    private PlanetOriginEpochState? originEpoch;
    private PlanetOriginCanonicalRockCells? spatialRockEpoch;
    private PlanetOriginSpatialSurfaceSnapshot? cachedSpatialRockProjection;
    private PlanetOriginSurfaceRockPhaseField? cachedSpatialRockField;

    /// <summary>Immutable Earth-origin epoch projected onto the canonical main planet, when selected.</summary>
    public PlanetOriginEpochState? PublishedOriginEpoch => originEpoch;

    /// <summary>Accepted physical regional rock field with the same canonical epoch revision.</summary>
    public PlanetOriginCanonicalRockCells? PublishedRegionalRock => spatialRockEpoch;

    /// <summary>
    /// Commits the regional and planetary views together. Any projection
    /// error restores both; the regional phase atlas can never be newer
    /// than the actual physical epoch or be accepted independently.
    /// </summary>
    public PlanetRenderSnapshot PublishRegionalRock(PlanetOriginCanonicalRockCells candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (hypsometryCache is null)
            throw new InvalidOperationException("Regional rock publication requires real terrain hypsometry.");

        var previous = originEpoch;
        var previousRock = spatialRockEpoch;
        var previousAtlas = cachedSpatialRockProjection;
        var previousField = cachedSpatialRockField;
        try
        {
            // Preserve the established monotonic publication contract.
            _ = PublishOriginEpoch(candidate.Epoch);
            spatialRockEpoch = candidate;
            cachedSpatialRockField = null;
            cachedSpatialRockProjection = null;
            return CreateSnapshot();
        }
        catch
        {
            originEpoch = previous;
            spatialRockEpoch = previousRock;
            cachedSpatialRockProjection = previousAtlas;
            cachedSpatialRockField = previousField;
            throw;
        }
    }

    /// <summary>
    /// Publishes an already conserved, energy-coupled candidate using the real terrain and water-volume model.
    /// A failing snapshot leaves the previously published epoch untouched.
    /// </summary>
    public PlanetRenderSnapshot PublishOriginEpoch(PlanetOriginEpochState epoch)
    {
        ArgumentNullException.ThrowIfNull(epoch);
        if (hypsometryCache is null)
        {
            throw new InvalidOperationException("Earth-origin publication requires canonical physical terrain hypsometry.");
        }

        if (originEpoch is { } published && (epoch.Revision <= published.Revision ||
            epoch.Clock.YearsSinceSolarSystemFormation < published.Clock.YearsSinceSolarSystemFormation ||
            (epoch.Clock.YearsSinceSolarSystemFormation == published.Clock.YearsSinceSolarSystemFormation &&
                epoch.Clock.MicrosecondsIntoYear <= published.Clock.MicrosecondsIntoYear)))
        {
            throw new InvalidOperationException("A canonical planet may only publish a newer geological revision and clock.");
        }

        var physics = PlanetPhysicsCalculator.Calculate(state.PhysicalParameters);
        _ = PlanetOriginSurfaceProjection.Create(epoch, state.PhysicalParameters.RadiusMeters, physics.SurfaceGravityMetersPerSecondSquared);
        var previous = originEpoch;
        var previousRegional = spatialRockEpoch;
        try
        {
            originEpoch = epoch;
            spatialRockEpoch = null;
            return CreateSnapshot();
        }
        catch
        {
            originEpoch = previous;
            spatialRockEpoch = previousRegional;
            throw;
        }
    }

    /// <summary>
    /// Advance physical time through the existing bounded, conservative integrator and publish
    /// the new main-planet snapshot only after every substep and terrain projection succeeds.
    /// A caller must provide actual re-evaluated process rates; this method invents none.
    /// </summary>
    public PlanetRenderSnapshot AdvanceOriginEpochAdaptive(
        PlanetOriginDuration duration,
        PlanetOriginAdaptiveIntegrationOptions options,
        Func<PlanetOriginEnergyLedger, PlanetOriginDuration, PlanetOriginEnergyStep> propose,
        CancellationToken cancellationToken = default)
    {
        var current = originEpoch ?? throw new InvalidOperationException("Publish a coupled Earth-origin epoch before advancing geological time.");
        var next = current.AdvanceCoupledAdaptive(duration, options, propose, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return PublishOriginEpoch(next);
    }

    public PlanetRenderSnapshot ClearOriginEpoch()
    {
        originEpoch = null;
        spatialRockEpoch = null;
        cachedSpatialRockProjection = null;
        cachedSpatialRockField = null;
        return CreateSnapshot();
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
        originEpoch = null;
        state.Reseed(seed);
        surfaceView = null;
        localAnchorDirection = null;
        cachedLocalSurfaceKey = null;
        cachedLocalSurface = null;
        surfaceMeshCache.Clear();
    }

    public PlanetRenderSnapshot ResetEarthReference()
    {
        originEpoch = null;
        state.ResetEarthReference();
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
        originEpoch = null;
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
        var origin = originEpoch is null ? null : PlanetOriginSurfaceProjection.Create(
            originEpoch, state.PhysicalParameters.RadiusMeters, basePhysics.SurfaceGravityMetersPerSecondSquared);
        // Legacy climate remains available for the frozen-world experiment, but
        // every canonical origin consumer must see water and gas masses from
        // the same accepted epoch—not only the WebGL-specific Origin payload.
        // Zero boiling point represents UNRESOLVED phase eligibility; a steam
        // atmosphere is never proof of rain or stable surface liquid.
        var water = origin is null ? climateResult.Water : new WaterPhaseSnapshot(
            origin.SurfaceIceMassKilograms,
            origin.SurfaceLiquidMassKilograms,
            origin.AtmosphericSteamMassKilograms,
            origin.SurfaceWaterIceFraction,
            origin.SurfaceWaterLiquidFraction,
            origin.SurfaceWaterVaporFraction,
            0.0,
            false);
        var originAtmosphereMass = origin is null ? 0.0 : origin.DryGasMassKilograms + origin.AtmosphericSteamMassKilograms;
        var renderedAtmosphere = origin is null ? atmosphere : atmosphere with
        {
            SurfacePressurePascals = origin.SurfacePressurePascals,
            EarthAtmosphereMasses = originAtmosphereMass / EarthAtmosphereReference.TotalMassKilograms
        };
        var seaLevelMeters = CalculateSeaLevelMeters(water.LiquidMassKilograms);
        var atmosphereDensity = CalculateAtmosphereDensity(renderedAtmosphere.SurfacePressurePascals);
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
        return new PlanetRenderSnapshot(
            surfaceTiles,
            seaLevelMeters,
            atmosphereDensity,
            state.Seed,
            state.PhysicalParameters,
            climateResult.Physics,
            state.AtmosphereParameters,
            renderedAtmosphere,
            climateResult.Climate,
            climateResult.Feedback,
            state.WaterParameters,
            water,
            localSurface,
            terrainRevision,
            origin,
            CreateSpatialRockProjection());
    }

    private PlanetOriginSpatialSurfaceSnapshot? CreateSpatialRockProjection()
    {
        if (spatialRockEpoch is not { } region) return null;
        if (!ReferenceEquals(originEpoch, region.Epoch))
            throw new InvalidOperationException("Regional rock must have the same revision as the canonical world.");
        if (!ReferenceEquals(cachedSpatialRockField, region.Field) ||
            cachedSpatialRockProjection is null || cachedSpatialRockProjection.Revision != originEpoch.Revision)
        {
            cachedSpatialRockProjection = PlanetOriginSpatialSurfaceProjection.Create(region);
            cachedSpatialRockField = region.Field;
        }

        return cachedSpatialRockProjection;
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

    private double CalculateSeaLevelMeters(double liquidMassKilograms)
    {
        // Frozen/dry worlds should not sample the full globe at startup.
        if (liquidMassKilograms <= 0.0)
        {
            return MinimumVisualSeaLevelMeters;
        }

        // Tests and legacy manually constructed experiences can retain their
        // previous behavior until opting into the shared physical model.
        if (hypsometryCache is null)
        {
            return CalculateVisualSeaLevelMeters(liquidMassKilograms);
        }

        var planetRadiusMeters = state.PhysicalParameters.RadiusMeters;
        var terrainVolume = hypsometryCache.GetOrBuild(
            SeaLevelSamplingGridLevel, state.Seed, planetRadiusMeters, CancellationToken.None);
        var liquidVolumeCubicMeters = liquidMassKilograms / LiquidWaterDensityKilogramsPerCubicMeter;
        return terrainVolume.SolveSeaLevelMeters(liquidVolumeCubicMeters);
    }

    private static double CalculateVisualSeaLevelMeters(double liquidMassKilograms)
    {
        var liquidEarthHydrospheres = liquidMassKilograms / EarthWaterReference.TotalHydrosphereMassKilograms;
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

    private void RequireLegacyClimateMode()
    {
        if (originEpoch is not null)
        {
            throw new InvalidOperationException(
                "Earth-origin changes must pass through the coupled geological epoch, not legacy climate controls.");
        }
    }

    private readonly record struct LocalSurfaceCacheKey(
        PlanetSurfaceAddress AnchorAddress,
        double PatchSizeMeters,
        int CellsPerAxis,
        int Seed,
        double PlanetRadiusMeters,
        long GeologyRevision);
}