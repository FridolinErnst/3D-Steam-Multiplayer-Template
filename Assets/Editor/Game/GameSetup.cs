using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Game.Editor.Setup
{
    /// <summary>
    /// Runs every <see cref="GameSetupStep"/>-marked setup class in order (menu <c>Game/Setup/Run All</c>), then the
    /// <see cref="MissingReferenceValidator"/> and the <see cref="RequiredFieldValidator"/>. Stops at the first failing step, and after step 00 when it generated new
    /// atom code (a recompile is needed before the remaining steps can see the generated types).
    /// </summary>
    public static class GameSetup
    {
        /// <summary>One discovered step: its order, declaring type and <c>Run()</c> method.</summary>
        internal readonly struct Step
        {
            public readonly int Order;
            public readonly Type Type;
            public readonly MethodInfo Run;

            public Step(int order, Type type, MethodInfo run)
            {
                Order = order;
                Type = type;
                Run = run;
            }
        }

        /// <summary>Runs all steps and the validator; returns the combined log.</summary>
        [MenuItem("Game/Setup/Run All")]
        public static void RunAllMenu()
        {
            Debug.Log(RunAll());
        }

        /// <summary>Runs all steps in order, then the missing reference validator; returns the combined log.</summary>
        public static string RunAll()
        {
            var log = new StringBuilder("Game setup — Run All\n");
            List<Step> steps = DiscoverSteps(log);

            foreach (Step step in steps)
            {
                log.Append("== [").Append(step.Order.ToString("00")).Append("] ").Append(step.Type.Name).Append('\n');
                try
                {
                    object result = step.Run.Invoke(null, null);
                    log.Append(result as string).Append('\n');
                }
                catch (TargetInvocationException tie) when (tie.InnerException != null)
                {
                    log.Append("FAILED: ").Append(tie.InnerException).Append('\n');
                    Debug.LogException(tie.InnerException);
                    log.Append("Run All stopped; fix the failure and run again.\n");
                    return log.ToString();
                }

                if (step.Type == typeof(AtomGenerationSetup) && AtomGenerationSetup.LastRunGenerated)
                {
                    log.Append("New atom code was generated. Let Unity recompile, then run 'Game/Setup/Run All' again.\n");
                    return log.ToString();
                }
            }

            log.Append("== Validation\n").Append(MissingReferenceValidator.Run()).Append('\n');
            log.Append(RequiredFieldValidator.Run()).Append('\n');
            return log.ToString();
        }

        /// <summary>
        /// Finds every class marked with <see cref="GameSetupStep"/> that has <c>public static string Run()</c>,
        /// sorted by order then full name. Invalid classes are reported in <paramref name="log"/> and skipped.
        /// </summary>
        internal static List<Step> DiscoverSteps(StringBuilder log = null)
        {
            var steps = new List<Step>();
            foreach (Type type in TypeCache.GetTypesWithAttribute<GameSetupStep>())
            {
                var attr = (GameSetupStep)Attribute.GetCustomAttribute(type, typeof(GameSetupStep), false);
                if (attr == null) continue;

                MethodInfo run = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                if (run == null || run.ReturnType != typeof(string))
                {
                    log?.Append("SKIPPED ").Append(type.FullName).Append(": no 'public static string Run()'.\n");
                    continue;
                }
                steps.Add(new Step(attr.Order, type, run));
            }

            steps.Sort(CompareSteps);
            return steps;
        }

        private static int CompareSteps(Step a, Step b)
        {
            int byOrder = a.Order.CompareTo(b.Order);
            return byOrder != 0 ? byOrder : string.CompareOrdinal(a.Type.FullName, b.Type.FullName);
        }
    }
}
