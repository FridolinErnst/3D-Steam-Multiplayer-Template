using NUnit.Framework;

namespace Game.Core.Contracts.Tests
{
    public sealed class SeededRandomTests
    {
        [Test]
        public void Same_Seed_Produces_Same_Sequence()
        {
            var a = new SeededRandom(1234);
            var b = new SeededRandom(1234);

            for (int i = 0; i < 100; i++)
            {
                Assert.AreEqual(a.Value(), b.Value());
                Assert.AreEqual(a.Range(0, 1000), b.Range(0, 1000));
            }
        }

        [Test]
        public void Different_Seeds_Produce_Different_Sequences()
        {
            var a = new SeededRandom(1);
            var b = new SeededRandom(2);

            bool anyDifferent = false;
            for (int i = 0; i < 20; i++)
                anyDifferent |= a.Range(0, 1000000) != b.Range(0, 1000000);

            Assert.IsTrue(anyDifferent);
        }

        [Test]
        public void Seed_Is_Exposed()
        {
            Assert.AreEqual(77, new SeededRandom(77).Seed);
        }

        [Test]
        public void Value_Is_In_Half_Open_Unit_Interval()
        {
            var r = new SeededRandom(42);
            for (int i = 0; i < 10000; i++)
            {
                float v = r.Value();
                Assert.GreaterOrEqual(v, 0f);
                Assert.Less(v, 1f);
            }
        }

        [Test]
        public void Value_Mean_Is_Roughly_Half()
        {
            var r = new SeededRandom(7);
            double sum = 0;
            const int n = 10000;
            for (int i = 0; i < n; i++) sum += r.Value();

            Assert.AreEqual(0.5, sum / n, 0.02);
        }

        [Test]
        public void Range_Stays_Within_Bounds_And_Hits_Every_Value()
        {
            var r = new SeededRandom(99);
            var seen = new bool[5];
            for (int i = 0; i < 1000; i++)
            {
                int v = r.Range(3, 8);
                Assert.GreaterOrEqual(v, 3);
                Assert.Less(v, 8);
                seen[v - 3] = true;
            }

            for (int i = 0; i < seen.Length; i++)
                Assert.IsTrue(seen[i], $"Value {i + 3} was never produced.");
        }

        [Test]
        public void Range_With_Single_Value_Returns_It()
        {
            var r = new SeededRandom(5);
            for (int i = 0; i < 10; i++)
                Assert.AreEqual(4, r.Range(4, 5));
        }

        [Test]
        public void Range_With_Empty_Or_Inverted_Bounds_Returns_Min_Without_Throwing()
        {
            var r = new SeededRandom(5);
            Assert.AreEqual(6, r.Range(6, 6));
            Assert.AreEqual(9, r.Range(9, 2));
        }

        [Test]
        public void Implements_IRandomSource()
        {
            IRandomSource source = new SeededRandom(0);
            Assert.IsNotNull(source);
        }
    }
}
