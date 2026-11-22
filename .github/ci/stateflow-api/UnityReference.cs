// Compilation-only signatures used by the checked runtime assemblies.
// Unity integration runs in the separate Editor and Player jobs.
using System;
using System.Reflection;

namespace UnityEngine
{
    public readonly struct EntityId
    {
        public static implicit operator EntityId(int instanceId) => throw new NotSupportedException();
    }

    public class Object
    {
        public string name { get; set; }
        public int GetInstanceID() => throw new NotSupportedException();
        public static T Instantiate<T>(T original) where T : Object => throw new NotSupportedException();
        public static void Destroy(Object value) => throw new NotSupportedException();
        public static void DestroyImmediate(Object value) => throw new NotSupportedException();
    }

    public class Component : Object
    {
        public Transform transform => throw new NotSupportedException();
        public T GetComponent<T>() where T : Component => throw new NotSupportedException();
    }

    public class MonoBehaviour : Component { }

    public class Transform : Component
    {
        public Transform parent => throw new NotSupportedException();
    }

    public struct Vector2
    {
        public float x;
        public float y;
    }

    public static class Time
    {
        public static float deltaTime => throw new NotSupportedException();
        public static float fixedDeltaTime => throw new NotSupportedException();
        public static int frameCount => throw new NotSupportedException();
    }

    public static class Application
    {
        public static bool isPlaying => throw new NotSupportedException();
    }
    public class ScriptableObject : Object { }

    public abstract class PropertyAttribute : Attribute { }

    public sealed class MinAttribute : PropertyAttribute
    {
        public MinAttribute(float min) { }
    }
    public sealed class HideInInspector : Attribute { }
    public sealed class SerializeField : Attribute { }
    public sealed class SerializeReference : Attribute { }
    public sealed class DisallowMultipleComponent : Attribute { }
    public sealed class AddComponentMenu : Attribute
    {
        public AddComponentMenu(string name) { }
    }

    public sealed class CreateAssetMenuAttribute : Attribute
    {
        public string fileName;
        public string menuName;
    }

    public enum RuntimeInitializeLoadType { SubsystemRegistration, BeforeSceneLoad, AfterSceneLoad }
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) { }
    }

    public static class Debug
    {
        public static void LogWarning(object message) => throw new NotSupportedException();
        public static void LogException(Exception exception, Object context) => throw new NotSupportedException();
        public static void LogError(object message) => throw new NotSupportedException();
    }
}

namespace UnityEngine.Scripting
{
    public sealed class PreserveAttribute : Attribute { }
}

namespace Unity.Collections.LowLevel.Unsafe
{
    public static unsafe class UnsafeUtility
    {
        public static int GetFieldOffset(FieldInfo field) => throw new NotSupportedException();
        public static int SizeOf(Type type) => throw new NotSupportedException();
        public static void* PinGCObjectAndGetAddress(object value, out ulong handle) => throw new NotSupportedException();
        public static void MemCpy(void* destination, void* source, long size) => throw new NotSupportedException();
        public static void ReleaseGCObject(ulong handle) => throw new NotSupportedException();
    }
}
