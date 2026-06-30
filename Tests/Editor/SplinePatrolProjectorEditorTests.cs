using System;
using System.IO;
using System.Reflection;
using CoCoFlow.Editor.Gameplay.Enemy;
using CoCoFlow.Runtime.Gameplay.Character;
using NUnit.Framework;
using UnityEditor.PackageManager;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace CoCoFlow.Tests.Editor.Enemy
{
    public class SplinePatrolProjectorEditorTests
    {
        private const string EnemySamplePrefabPath =
            "Samples~/Enemy Samples/CoCoFlow/Enemy Samples/Prefabs/P_Enemy_00.prefab";

        #region Public API

        [Test]
        public void EvaluateWorldPositionMatchesSplineContainerTransform()
        {
            var root = new GameObject("Spline Projector Editor Test");
            try
            {
                var container = root.AddComponent<SplineContainer>();
                container.Spline = new Spline
                {
                    new BezierKnot(new float3(1f, 0f, 0f)),
                    new BezierKnot(new float3(1f, 0f, 2f)),
                    new BezierKnot(new float3(3f, 0f, 2f))
                };
                root.transform.position = new Vector3(10f, 2f, -4f);
                root.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                root.transform.localScale = new Vector3(2f, 1f, 3f);

                Vector3 localPosition = (Vector3)container.EvaluatePosition(0f);
                Vector3 expected = root.transform.TransformPoint(localPosition);
                Vector3 actual = InvokeEvaluateWorldPosition(container, 0f);

                Assert.Less(Vector3.Distance(expected, actual), 0.0001f);
                Assert.Greater(Vector3.Distance(localPosition, actual), 0.1f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void EnemySamplePrefabKeepsGravityWithGroundLayerMask()
        {
            string prefabPath = Path.Combine(ResolvePackagePath(), EnemySamplePrefabPath);
            Assert.IsTrue(File.Exists(prefabPath), prefabPath);

            string prefabText = File.ReadAllText(prefabPath);
            int locomotionIndex = prefabText.IndexOf(
                "m_EditorClassIdentifier: CoCoFlow.Runtime.Gameplay.Character::CoCoFlow.Runtime.Gameplay.Character.CharacterLocomotion",
                StringComparison.Ordinal);
            Assert.GreaterOrEqual(locomotionIndex, 0);

            string locomotionSection = prefabText.Substring(locomotionIndex);
            int nextComponentIndex = locomotionSection.IndexOf("--- !u!", StringComparison.Ordinal);
            if (nextComponentIndex >= 0)
            {
                locomotionSection = locomotionSection.Substring(0, nextComponentIndex);
            }

            Assert.That(locomotionSection, Does.Contain("isUsingGravity: 1"));
            Assert.That(locomotionSection, Does.Contain("m_Bits: 1"));
        }

        #endregion

        #region Internal Logic

        private static Vector3 InvokeEvaluateWorldPosition(SplineContainer container, float progress)
        {
            var method = typeof(SplinePatrolProjectorEditor).GetMethod(
                "EvaluateWorldPosition",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            return (Vector3)method.Invoke(null, new object[] { container, progress });
        }

        private static string ResolvePackagePath()
        {
            var packageInfo = PackageInfo.FindForAssembly(typeof(CharacterLocomotion).Assembly);
            Assert.IsNotNull(packageInfo);
            return packageInfo.resolvedPath;
        }

        #endregion
    }
}
