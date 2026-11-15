namespace Game.Core
{
    /// <summary>Deterministic IRandomSource backed by a seeded System.Random (used by tests and seeded runs).</summary>
    public sealed class SeededRandom : IRandomSource
    {
        // Largest float strictly below 1; guards the double-to-float cast rounding up to 1.
        private const float MaxBelowOne = 0.99999994f;

        private readonly System.Random _rng;

        public SeededRandom(int seed)
        {
            Seed = seed;
            _rng = new System.Random(seed);
        }

        /// <summary>The seed this source was created with.</summary>
        public int Seed { get; }

        /// <inheritdoc />
        public float Value()
        {
            float v = (float)_rng.NextDouble();
            return v >= 1f ? MaxBelowOne : v;
        }

        /// <inheritdoc />
        public int Range(int minInclusive, int maxExclusive)
        {
            // An empty or inverted range yields the minimum instead of throwing.
            if (maxExclusive <= minInclusive) return minInclusive;
            return _rng.Next(minInclusive, maxExclusive);
        }
    }
}
