using System;

namespace Game.Editor.Setup
{
    /// <summary>
    /// Marks a static setup class as a step of <see cref="GameSetup.RunAll"/>. The class must expose
    /// <c>public static string Run()</c>; steps run in ascending <see cref="Order"/> (ties by full type name).
    /// Usage: <c>[GameSetupStep(order: 2)]</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class GameSetupStep : Attribute
    {
        /// <summary>Creates the marker with the step's run order (the NN of its menu item).</summary>
        public GameSetupStep(int order)
        {
            Order = order;
        }

        /// <summary>Run order; matches the two-digit number in the step's menu item.</summary>
        public int Order { get; }
    }
}
