using Game.Core;

namespace Game.Contracts
{
    /// <summary>Default runtime <see cref="IRandomSource"/> backed by UnityEngine.Random (tests inject SeededRandom instead).</summary>
    public sealed class UnityRandomSource : IRandomSource
    {
        // Largest float below 1; UnityEngine.Random.value is inclusive of 1, the contract is [0,1).
        private const float BelowOne = 0.99999994f;

        public float Value()
        {
            float v = UnityEngine.Random.value;
            return v >= 1f ? BelowOne : v;
        }

        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            return UnityEngine.Random.Range(minInclusive, maxExclusive);
        }
    }
}
