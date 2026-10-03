namespace PlanetForge.Application.Rendering;

public sealed record PlanetRenderSnapshot(PlanetMesh Mesh, double SeaLevel, double AtmosphereDensity, int Seed);
