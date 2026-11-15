namespace Game.Core
{
    /// <summary>Injectable source of randomness so cores stay deterministic in tests.</summary>
    public interface IRandomSource
    {
        /// <summary>Uniform float in [0, 1).</summary>
        float Value();

        /// <summary>Uniform int in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>); returns the minimum for an empty range.</summary>
        int Range(int minInclusive, int maxExclusive);
    }
}
