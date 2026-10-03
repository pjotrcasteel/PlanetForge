namespace PlanetForge.Domain.Planets;

public sealed class PlanetState
{
    private const double MinimumSeaLevel = -0.035;
    private const double MaximumSeaLevel = 0.035;
    private const double MinimumAtmosphereDensity = 0.0;
    private const double MaximumAtmosphereDensity = 1.0;

    public PlanetState(int seed = 24061984, double seaLevel = -0.004, double atmosphereDensity = 0.62)
    {
        Seed = seed;
        SeaLevel = Math.Clamp(seaLevel, MinimumSeaLevel, MaximumSeaLevel);
        AtmosphereDensity = Math.Clamp(atmosphereDensity, MinimumAtmosphereDensity, MaximumAtmosphereDensity);
    }

    public int Seed { get; private set; }

    public double SeaLevel { get; private set; }

    public double AtmosphereDensity { get; private set; }

    public void ChangeSeaLevel(double delta) => SeaLevel = Math.Clamp(SeaLevel + delta, MinimumSeaLevel, MaximumSeaLevel);

    public void ChangeAtmosphereDensity(double delta) => AtmosphereDensity = Math.Clamp(AtmosphereDensity + delta, MinimumAtmosphereDensity, MaximumAtmosphereDensity);

    public void Reseed(int seed) => Seed = seed;
}
