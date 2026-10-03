using PlanetForge.Application.Rendering;
using PlanetForge.Domain.Planets;

namespace PlanetForge.Application.Planets;

public sealed class LivingRockExperience(PlanetMeshBuilder meshBuilder)
{
    private readonly PlanetState state = new();

    public PlanetRenderSnapshot CreateSnapshot() => new(meshBuilder.Build(state.Seed), state.SeaLevel, state.AtmosphereDensity, state.Seed);

    public PlanetRenderSnapshot RaiseSeaLevel()
    {
        state.ChangeSeaLevel(0.004);
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot LowerSeaLevel()
    {
        state.ChangeSeaLevel(-0.004);
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot IncreaseAtmosphere()
    {
        state.ChangeAtmosphereDensity(0.08);
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot DecreaseAtmosphere()
    {
        state.ChangeAtmosphereDensity(-0.08);
        return CreateSnapshot();
    }

    public PlanetRenderSnapshot Reseed()
    {
        state.Reseed(unchecked((state.Seed * 397) ^ 7919));
        return CreateSnapshot();
    }
}
