using System.Reflection;
using CoCoFlow.Editor.Gameplay.Enemy;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace CoCoFlow.Tests.Editor.Enemy
{
    public class SplinePatrolProjectorEditorTests
    {
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
                Object.DestroyImmediate(root);
            }
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

        #endregion
    }
}
