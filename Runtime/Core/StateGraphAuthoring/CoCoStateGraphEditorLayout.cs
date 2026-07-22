using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoCoFlow.Runtime.Core
{
    [Serializable]
    internal sealed class CoCoStateGraphEditorLayout
    {
        internal const uint CurrentVersion = 1U;

        [SerializeField] private uint version = CurrentVersion;
        [SerializeField] private List<CoCoStateGraphStateLayoutRecord> statePositions =
            new List<CoCoStateGraphStateLayoutRecord>();

        internal uint Version => version == 0U ? CurrentVersion : version;
        internal bool IsSupported => version == 0U || version == CurrentVersion;
        internal IReadOnlyList<CoCoStateGraphStateLayoutRecord> StatePositions =>
            statePositions ?? (statePositions = new List<CoCoStateGraphStateLayoutRecord>());

        internal bool TryGetPosition(CoCoSerializedId128 stateId, out Vector2 position)
        {
            position = default;
            if (!stateId.IsValid || statePositions == null)
            {
                return false;
            }

            bool found = false;
            for (int index = 0; index < statePositions.Count; index++)
            {
                CoCoStateGraphStateLayoutRecord record = statePositions[index];
                if (record == null || record.StateId != stateId)
                {
                    continue;
                }

                if (found)
                {
                    position = default;
                    return false;
                }

                position = record.LocalPosition;
                found = true;
            }

            return found && IsFinite(position);
        }

        internal void SetPosition(CoCoSerializedId128 stateId, Vector2 position)
        {
            if (!IsSupported)
            {
                throw new InvalidOperationException(
                    $"EditorLayout version {version} is newer than the supported version {CurrentVersion}.");
            }

            if (!stateId.IsValid)
            {
                throw new ArgumentException("A valid State ID is required.", nameof(stateId));
            }

            if (!IsFinite(position))
            {
                throw new ArgumentOutOfRangeException(nameof(position), "A finite local position is required.");
            }

            version = CurrentVersion;
            statePositions ??= new List<CoCoStateGraphStateLayoutRecord>();
            int firstMatch = -1;
            for (int index = statePositions.Count - 1; index >= 0; index--)
            {
                CoCoStateGraphStateLayoutRecord record = statePositions[index];
                if (record == null || record.StateId != stateId)
                {
                    continue;
                }

                if (firstMatch < 0)
                {
                    firstMatch = index;
                }
                else
                {
                    statePositions.RemoveAt(index);
                    firstMatch--;
                }
            }

            if (firstMatch >= 0)
            {
                statePositions[firstMatch].LocalPosition = position;
            }
            else
            {
                statePositions.Add(new CoCoStateGraphStateLayoutRecord(stateId, position));
            }
        }

        internal void Remove(CoCoSerializedId128 stateId)
        {
            if (statePositions == null)
            {
                return;
            }

            for (int index = statePositions.Count - 1; index >= 0; index--)
            {
                CoCoStateGraphStateLayoutRecord record = statePositions[index];
                if (record == null || record.StateId == stateId)
                {
                    statePositions.RemoveAt(index);
                }
            }
        }

        internal void RemapStateIds(
            IReadOnlyDictionary<CoCoSerializedId128, CoCoSerializedId128> stateIdRemaps)
        {
            if (stateIdRemaps == null || statePositions == null)
            {
                return;
            }

            foreach (CoCoStateGraphStateLayoutRecord record in statePositions)
            {
                if (record != null &&
                    record.StateId.IsValid &&
                    stateIdRemaps.TryGetValue(record.StateId, out CoCoSerializedId128 remapped))
                {
                    record.StateId = remapped;
                }
            }
        }

        internal void Repair(ISet<CoCoSerializedId128> validStateIds)
        {
            version = CurrentVersion;
            statePositions ??= new List<CoCoStateGraphStateLayoutRecord>();
            var seen = new HashSet<CoCoSerializedId128>();
            for (int index = statePositions.Count - 1; index >= 0; index--)
            {
                CoCoStateGraphStateLayoutRecord record = statePositions[index];
                if (record == null ||
                    !record.StateId.IsValid ||
                    validStateIds == null ||
                    !validStateIds.Contains(record.StateId) ||
                    !IsFinite(record.LocalPosition) ||
                    !seen.Add(record.StateId))
                {
                    statePositions.RemoveAt(index);
                }
            }
        }

        private static bool IsFinite(Vector2 value) =>
            !float.IsNaN(value.x) &&
            !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) &&
            !float.IsInfinity(value.y);
    }

    [Serializable]
    internal sealed class CoCoStateGraphStateLayoutRecord
    {
        [SerializeField, HideInInspector] private CoCoSerializedId128 stateId;
        [SerializeField] private Vector2 localPosition;

        internal CoCoStateGraphStateLayoutRecord(CoCoSerializedId128 stateId, Vector2 localPosition)
        {
            this.stateId = stateId;
            this.localPosition = localPosition;
        }

        internal CoCoSerializedId128 StateId
        {
            get => stateId;
            set => stateId = value;
        }

        internal Vector2 LocalPosition
        {
            get => localPosition;
            set => localPosition = value;
        }
    }
}
