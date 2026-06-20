using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Splines;
using CoCoFlow.Runtime.Gameplay.Enemy;

namespace CoCoFlow.Editor.Gameplay.Enemy
{
    [CustomEditor(typeof(EnemySpline))]
    public class SplinePatrolProjectorEditor : UnityEditor.Editor
    {
        private void OnSceneGUI()
        {
            SerializedProperty splineProp = serializedObject.FindProperty("splineContainer");
            SplineContainer spline = splineProp.objectReferenceValue as SplineContainer;
            if (spline == null) return;

            int sampleCount = 20;
            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)(sampleCount - 1);
                Vector3 splinePoint = (Vector3)spline.EvaluatePosition(t);

                Handles.color = Color.red;
                Handles.SphereHandleCap(0, splinePoint, Quaternion.identity, 0.05f, EventType.Repaint);

                if (NavMesh.SamplePosition(splinePoint, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                {
                    Handles.color = Color.yellow;
                    Handles.DrawSolidDisc(hit.position, Vector3.up, 0.1f);

                    Handles.color = new Color(1f, 1f, 0f, 0.5f);
                    Handles.DrawLine(splinePoint, hit.position);
                }
            }
        }
    }
}
