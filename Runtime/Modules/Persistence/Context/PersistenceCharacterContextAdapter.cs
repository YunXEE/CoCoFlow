using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Persistence.Context
{
    public sealed class PersistenceCharacterContextAdapter : IPersistenceContextAdapter
    {
        private const string CharacterContextType = "CoCoFlow.Runtime.Gameplay.Character.CharacterContext";

        public bool CanCapture(ICoCoContext context)
        {
            return context is CoCoEntityContext entityContext &&
                   PersistenceContextReflection.IsOrDerivesFrom(entityContext.GetType(), CharacterContextType);
        }

        public bool CanApply(PersistenceContextRecord record, ICoCoContext context)
        {
            return CanCapture(context);
        }

        public PersistenceContextRecord Capture(string stableEntityId, ICoCoContext context)
        {
            var entityContext = (CoCoEntityContext)context;
            var record = new PersistenceContextRecord { stableEntityId = stableEntityId };
            PersistenceContextReflection.CaptureEntityBase(record, entityContext);

            object motion = PersistenceContextReflection.GetPropertyValue(entityContext, "Motion");
            if (motion != null)
            {
                record.Vector3Facts["motion.position"] =
                    PersistenceContextReflection.ToData((Vector3)PersistenceContextReflection.GetFieldValue(motion, "position"));
                record.QuaternionFacts["motion.rotation"] =
                    PersistenceContextReflection.ToData((Quaternion)PersistenceContextReflection.GetFieldValue(motion, "rotation"));
                record.Vector3Facts["motion.velocity"] =
                    PersistenceContextReflection.ToData((Vector3)PersistenceContextReflection.GetFieldValue(motion, "velocity"));
                record.BoolFacts["motion.isGrounded"] =
                    (bool)PersistenceContextReflection.GetFieldValue(motion, "isGrounded");
            }

            object resources = PersistenceContextReflection.GetPropertyValue(entityContext, "Resources");
            if (resources != null)
            {
                record.FloatFacts["resources.maxHealth"] =
                    (float)PersistenceContextReflection.GetPropertyValue(resources, "MaxHealth");
                record.FloatFacts["resources.currentHealth"] =
                    (float)PersistenceContextReflection.GetPropertyValue(resources, "CurrentHealth");
            }

            return record;
        }

        public void Apply(PersistenceContextRecord record, ICoCoContext context)
        {
            var entityContext = (CoCoEntityContext)context;
            PersistenceContextReflection.ApplyEntityBase(record, entityContext);

            object motion = PersistenceContextReflection.GetPropertyValue(entityContext, "Motion");
            if (motion != null)
            {
                if (record.Vector3Facts.TryGetValue("motion.position", out var position))
                {
                    PersistenceContextReflection.SetFieldValue(
                        motion,
                        "position",
                        PersistenceContextReflection.ToVector3(position));
                }

                if (record.QuaternionFacts.TryGetValue("motion.rotation", out var rotation))
                {
                    PersistenceContextReflection.SetFieldValue(
                        motion,
                        "rotation",
                        PersistenceContextReflection.ToQuaternion(rotation));
                }

                if (record.Vector3Facts.TryGetValue("motion.velocity", out var velocity))
                {
                    PersistenceContextReflection.SetFieldValue(
                        motion,
                        "velocity",
                        PersistenceContextReflection.ToVector3(velocity));
                }

                if (record.BoolFacts.TryGetValue("motion.isGrounded", out bool isGrounded))
                {
                    PersistenceContextReflection.SetFieldValue(motion, "isGrounded", isGrounded);
                }
            }

            object resources = PersistenceContextReflection.GetPropertyValue(entityContext, "Resources");
            if (resources != null)
            {
                if (record.FloatFacts.TryGetValue("resources.maxHealth", out float maxHealth))
                {
                    resources.GetType().GetProperty("MaxHealth")?.SetValue(resources, maxHealth);
                }

                if (record.FloatFacts.TryGetValue("resources.currentHealth", out float currentHealth))
                {
                    resources.GetType().GetProperty("CurrentHealth")?.SetValue(resources, currentHealth);
                }
            }
        }
    }
}
