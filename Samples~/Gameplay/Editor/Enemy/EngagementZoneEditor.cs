using UnityEditor;
using UnityEngine;
using CoCoFlow.Runtime.Gameplay.Enemy;

namespace CoCoFlow.Editor.Gameplay.Enemy
{
    [CustomEditor(typeof(EnemyEngagementZone))]
    public class EngagementZoneEditor : UnityEditor.Editor
    {
        private void OnSceneGUI()
        {
            EnemyEngagementZone zone = (EnemyEngagementZone)target;
            BoxCollider col = zone.GetComponent<BoxCollider>();
            if (col == null) return;

            Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;

            Matrix4x4 originalMatrix = Handles.matrix;
            Handles.matrix = col.transform.localToWorldMatrix;

            Vector3 center = col.center;
            Vector3 size = col.size;

            Handles.color = new Color(1f, 0f, 0f, 0.15f);
            Handles.DrawWireCube(center, size);

            Handles.color = new Color(1f, 0f, 0f, 0.5f);
            Handles.DrawWireCube(center, size * 1.01f);

            Handles.matrix = originalMatrix;
        }
    }
}
