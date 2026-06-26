using CoCoFlow.Runtime.Modules.Animation.Rig;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.Modules.Animation.Rig
{
    [CustomEditor(typeof(AnimRigCharacterController))]
    public class AnimRigCharacterControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(6f);
            EditorGUILayout.HelpBox(
                "Foot targets are regular Transforms. Wire them to Animation Rigging constraints in the character prefab; State Layer scripts should only call the controller operation API.",
                MessageType.Info);

            if (target is AnimRigCharacterController controller &&
                controller.Profile == null)
            {
                EditorGUILayout.HelpBox(
                    "No AnimRigCharacterProfile is assigned. Runtime defaults will be used, but prefabs should reference a profile asset.",
                    MessageType.Warning);
            }
        }
    }
}
