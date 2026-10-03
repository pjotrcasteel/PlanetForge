using System.Security.Cryptography;
using System.Text;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Domain.WorldGeneration;

public static class PlanetGeneratedPlacementIdentity
{
    public static PlanetGeneratedPlacementId Create(
        PlanetWorldIdentity world,
        PlanetSurfaceAddress address,
        string layer,
        int slot)
    {
        if (string.IsNullOrWhiteSpace(layer))
        {
            throw new ArgumentException("Placement layer is required.", nameof(layer));
        }

        if (slot < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "Placement slot must be zero or greater.");
        }

        var canonical = FormattableString.Invariant(
            $"v{world.GenerationVersion.Value}|s{world.Seed}|a{address.PrecisionBits}:{address.X}:{address.Y}|l{layer.Trim()}|p{slot}");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return new PlanetGeneratedPlacementId(Convert.ToHexString(hash.AsSpan(0, 16)));
    }
}