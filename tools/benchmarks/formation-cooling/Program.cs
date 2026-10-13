using System.Diagnostics;
using System.Security.Cryptography;
using PlanetForge.Application.Planets;
using PlanetForge.Application.Rendering;
using PlanetForge.Application.Surface;
using PlanetForge.Application.Surface.Hydrology;
using PlanetForge.Domain.Surface;
using PlanetForge.Infrastructure.Surface;
var provider=new PlanetTerrainRegionProvider(new Flat());
var experience=new PlanetExperience(new PlanetSurfaceMeshCache(new PlanetSurfaceMeshBuilder(new PlanetSurfaceTileSampler(provider),provider)),new PlanetLocalSurfacePatchSampler(provider),new PlanetLocalSurfaceMeshBuilder(),hypsometryCache:new PlanetHypsometryCache(new PlanetHypsometrySampler(provider),provider));
PlanetFormationSimulation.Start(experience,24061984);
var before=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();
for(var i=0;i<6;i++)PlanetFormationSimulation.Advance(experience,5);
watch.Stop();var allocated=GC.GetAllocatedBytesForCurrentThread()-before;
var world=experience.PublishedRegionalRock!;
using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
foreach(var c in world.Columns!.Columns)foreach(var l in c.Layers)writer.Write(l.StoredEnthalpyJoules);
Console.WriteLine($"elapsedMs={watch.Elapsed.TotalMilliseconds:F3}; allocatedBytes={allocated}; stateSHA256={Convert.ToHexString(SHA256.HashData(stream.ToArray()))}; time={world.Epoch.Clock.MicrosecondsIntoYear}; residual={world.Epoch.EnergyConservationResidualJoules:R}");
sealed class Flat:IPlanetElevationSource{public double SampleElevationMeters(PlanetVector direction,int seed)=>0;}
