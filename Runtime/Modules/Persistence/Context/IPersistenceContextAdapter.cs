using System;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Persistence
{
    public interface IPersistenceContextAdapter
    {
        bool CanCapture(ICoCoContext context);
        bool CanApply(PersistenceContextRecord record, ICoCoContext context);
        PersistenceContextRecord Capture(string stableEntityId, ICoCoContext context);
        void Apply(PersistenceContextRecord record, ICoCoContext context);
    }

    internal static class PersistenceContextReflection
    {
        public static bool IsOrDerivesFrom(Type type, string fullName)
        {
            while (type != null)
            {
                if (type.FullName == fullName) return true;
                type = type.BaseType;
            }

            return false;
        }

        public static object GetPropertyValue(object target, string propertyName)
        {
            if (target == null) return null;
            var property = target.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return property?.GetValue(target);
        }

        public static object GetFieldValue(object target, string fieldName)
        {
            if (target == null) return null;
            var field = FindField(target.GetType(), fieldName);
            return field?.GetValue(target);
        }

        public static void SetFieldValue(object target, string fieldName, object value)
        {
            if (target == null) return;
            var field = FindField(target.GetType(), fieldName);
            field?.SetValue(target, value);
        }

        public static void Invoke(object target, string methodName)
        {
            if (target == null) return;
            var method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            method?.Invoke(target, null);
        }

        public static PersistenceVector3Data ToData(Vector3 value)
        {
            return new PersistenceVector3Data { x = value.x, y = value.y, z = value.z };
        }

        public static PersistenceQuaternionData ToData(Quaternion value)
        {
            return new PersistenceQuaternionData { x = value.x, y = value.y, z = value.z, w = value.w };
        }

        public static Vector3 ToVector3(PersistenceVector3Data value)
        {
            return new Vector3(value.x, value.y, value.z);
        }

        public static Quaternion ToQuaternion(PersistenceQuaternionData value)
        {
            return new Quaternion(value.x, value.y, value.z, value.w);
        }

        public static void CaptureEntityBase(PersistenceContextRecord record, CoCoEntityContext context)
        {
            record.contextType = context.GetType().AssemblyQualifiedName;
            record.ownerId = context.Identity.OwnerId;
            record.entityTypeId = context.Identity.EntityTypeId;
            record.prefabKey = context.Identity.PrefabKey;
            record.lifecycleState = (int)context.Lifecycle.State;
            record.semanticStateId = context.SemanticStateId;
            record.actionStateId = context.ActionStateId;
            record.lastEventSequence = context.LastEventSequence;
        }

        public static void ApplyEntityBase(PersistenceContextRecord record, CoCoEntityContext context)
        {
            context.Identity.StableEntityId = record.stableEntityId;
            context.Identity.OwnerId = record.ownerId;
            context.Identity.EntityTypeId = record.entityTypeId;
            context.Identity.PrefabKey = record.prefabKey;
            SetFieldValue(context.Lifecycle, "state", (CoCoLifecycleState)record.lifecycleState);
            context.SemanticStateId = record.semanticStateId;
            context.ActionStateId = record.actionStateId;
            context.LastEventSequence = record.lastEventSequence;
        }

        private static FieldInfo FindField(Type type, string fieldName)
        {
            while (type != null)
            {
                var field = type.GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null) return field;
                type = type.BaseType;
            }

            return null;
        }
    }
}
