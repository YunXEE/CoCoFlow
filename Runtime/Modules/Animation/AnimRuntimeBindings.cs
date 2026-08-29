using System;
using CoCoFlow.Runtime.Animation.Contracts;
using CoCoFlow.Runtime.Core;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace CoCoFlow.Runtime.Modules.Animation
{
    public enum AnimEvaluationMode
    {
        Tick = 0,
        Step = 1
    }

    public enum AnimExactReplayStatus
    {
        Deferred = 1
    }

    [Serializable]
    public struct AnimParameterBinding
    {
        [SerializeField] private ulong bindingId;
        [SerializeField] private string parameterName;
        [SerializeField] private AnimParameterValueKind parameterKind;

        public ulong BindingId => bindingId;
        public string ParameterName => parameterName ?? string.Empty;
        public AnimParameterValueKind ParameterKind => parameterKind;
    }

    [Serializable]
    public struct AnimTriggerBinding
    {
        [SerializeField] private ulong bindingId;
        [SerializeField] private string parameterName;

        public ulong BindingId => bindingId;
        public string ParameterName => parameterName ?? string.Empty;
    }

    internal readonly struct AnimParameterTarget
    {
        internal AnimParameterTarget(
            AnimBindingId bindingId,
            int parameterHash,
            AnimParameterValueKind kind)
        {
            BindingId = bindingId;
            ParameterHash = parameterHash;
            Kind = kind;
        }

        internal AnimBindingId BindingId { get; }
        internal int ParameterHash { get; }
        internal AnimParameterValueKind Kind { get; }
    }

    internal readonly struct AnimTriggerTarget
    {
        internal AnimTriggerTarget(AnimBindingId bindingId, int parameterHash)
        {
            BindingId = bindingId;
            ParameterHash = parameterHash;
        }

        internal AnimBindingId BindingId { get; }
        internal int ParameterHash { get; }
    }

    internal static class AnimBindingRuntime
    {
        internal static bool TryBuildParameters(
            Animator animator,
            AnimParameterBinding[] bindings,
            out AnimParameterTarget[] targets,
            out CoCoDiagnostic diagnostic)
        {
            bindings ??= Array.Empty<AnimParameterBinding>();
            if (animator == null || bindings.Length > AnimContractLimits.ParameterLaneCount)
            {
                targets = null;
                diagnostic = Error(
                    "Animation parameter bindings require one Animator and at most 16 entries.");
                return false;
            }

            AnimatorControllerParameter[] controllerParameters = animator.parameters;
            targets = new AnimParameterTarget[bindings.Length];
            for (int index = 0; index < bindings.Length; index++)
            {
                AnimParameterBinding binding = bindings[index];
                if (!AnimBindingId.TryCreate(binding.BindingId, out AnimBindingId bindingId) ||
                    string.IsNullOrWhiteSpace(binding.ParameterName) ||
                    binding.ParameterKind < AnimParameterValueKind.Float ||
                    binding.ParameterKind > AnimParameterValueKind.Boolean ||
                    Contains(targets, index, bindingId))
                {
                    targets = null;
                    diagnostic = Error(
                        "Animation parameter bindings require unique non-zero ids, names and value kinds.");
                    return false;
                }

                int hash = Animator.StringToHash(binding.ParameterName);
                if (!TryFindParameter(controllerParameters, hash, out AnimatorControllerParameter parameter) ||
                    !Matches(parameter.type, binding.ParameterKind) ||
                    ContainsParameterHash(targets, index, hash))
                {
                    targets = null;
                    diagnostic = Error(
                        "Animation parameter bindings must target unique matching Animator parameters.");
                    return false;
                }

                targets[index] = new AnimParameterTarget(bindingId, hash, binding.ParameterKind);
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal static bool TryBuildTriggers(
            Animator animator,
            AnimTriggerBinding[] bindings,
            out AnimTriggerTarget[] targets,
            out CoCoDiagnostic diagnostic)
        {
            bindings ??= Array.Empty<AnimTriggerBinding>();
            if (animator == null || bindings.Length > AnimContractLimits.TriggerLaneCount)
            {
                targets = null;
                diagnostic = Error(
                    "Animation trigger bindings require one Animator and at most eight entries.");
                return false;
            }

            AnimatorControllerParameter[] controllerParameters = animator.parameters;
            targets = new AnimTriggerTarget[bindings.Length];
            for (int index = 0; index < bindings.Length; index++)
            {
                AnimTriggerBinding binding = bindings[index];
                if (!AnimBindingId.TryCreate(binding.BindingId, out AnimBindingId bindingId) ||
                    string.IsNullOrWhiteSpace(binding.ParameterName) ||
                    Contains(targets, index, bindingId))
                {
                    targets = null;
                    diagnostic = Error(
                        "Animation trigger bindings require unique non-zero ids and names.");
                    return false;
                }

                int hash = Animator.StringToHash(binding.ParameterName);
                if (!TryFindParameter(controllerParameters, hash, out AnimatorControllerParameter parameter) ||
                    parameter.type != AnimatorControllerParameterType.Trigger ||
                    ContainsTriggerHash(targets, index, hash))
                {
                    targets = null;
                    diagnostic = Error(
                        "Animation trigger bindings must target unique Trigger parameters.");
                    return false;
                }

                targets[index] = new AnimTriggerTarget(bindingId, hash);
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal static bool TryFind(
            AnimParameterTarget[] targets,
            AnimBindingId bindingId,
            out AnimParameterTarget target)
        {
            for (int index = 0; index < targets.Length; index++)
            {
                if (targets[index].BindingId == bindingId)
                {
                    target = targets[index];
                    return true;
                }
            }

            target = default;
            return false;
        }

        internal static bool ValidateParameters(
            IAnimParameterOperationSection section,
            AnimParameterTarget[] targets)
        {
            for (int lane = 0; lane < AnimContractLimits.ParameterLaneCount; lane++)
            {
                AnimParameterCommand command = AnimOperationLaneReader.Read(section, lane);
                if (command.Kind == AnimParameterValueKind.None)
                {
                    continue;
                }

                if (!command.IsValid ||
                    !TryFind(targets, command.BindingId, out AnimParameterTarget target) ||
                    target.Kind != command.Kind ||
                    HasEarlierParameter(section, lane, command.BindingId))
                {
                    return false;
                }
            }

            return true;
        }

        internal static bool TryFind(
            AnimTriggerTarget[] targets,
            AnimBindingId bindingId,
            out AnimTriggerTarget target)
        {
            for (int index = 0; index < targets.Length; index++)
            {
                if (targets[index].BindingId == bindingId)
                {
                    target = targets[index];
                    return true;
                }
            }

            target = default;
            return false;
        }

        internal static bool ValidateTriggers(
            IAnimTriggerOperationSection section,
            AnimTriggerTarget[] targets)
        {
            for (int lane = 0; lane < AnimContractLimits.TriggerLaneCount; lane++)
            {
                AnimTriggerCommand command = AnimOperationLaneReader.Read(section, lane);
                if (command.Kind == AnimTriggerCommandKind.None)
                {
                    continue;
                }

                if (!command.IsValid ||
                    !TryFind(targets, command.BindingId, out _) ||
                    HasEarlierTrigger(section, lane, command.BindingId))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Matches(
            AnimatorControllerParameterType controllerType,
            AnimParameterValueKind valueKind)
        {
            return (controllerType == AnimatorControllerParameterType.Float &&
                    valueKind == AnimParameterValueKind.Float) ||
                   (controllerType == AnimatorControllerParameterType.Int &&
                    valueKind == AnimParameterValueKind.Integer) ||
                   (controllerType == AnimatorControllerParameterType.Bool &&
                    valueKind == AnimParameterValueKind.Boolean);
        }

        private static bool HasEarlierParameter(
            IAnimParameterOperationSection section,
            int lane,
            AnimBindingId bindingId)
        {
            for (int previous = 0; previous < lane; previous++)
            {
                AnimParameterCommand command =
                    AnimOperationLaneReader.Read(section, previous);
                if (command.Kind != AnimParameterValueKind.None &&
                    command.BindingId == bindingId)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasEarlierTrigger(
            IAnimTriggerOperationSection section,
            int lane,
            AnimBindingId bindingId)
        {
            for (int previous = 0; previous < lane; previous++)
            {
                AnimTriggerCommand command =
                    AnimOperationLaneReader.Read(section, previous);
                if (command.Kind != AnimTriggerCommandKind.None &&
                    command.BindingId == bindingId)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryFindParameter(
            AnimatorControllerParameter[] parameters,
            int nameHash,
            out AnimatorControllerParameter parameter)
        {
            for (int index = 0; index < parameters.Length; index++)
            {
                if (parameters[index].nameHash == nameHash)
                {
                    parameter = parameters[index];
                    return true;
                }
            }

            parameter = null;
            return false;
        }

        private static bool Contains(
            AnimParameterTarget[] targets,
            int count,
            AnimBindingId id)
        {
            for (int index = 0; index < count; index++)
            {
                if (targets[index].BindingId == id)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsParameterHash(
            AnimParameterTarget[] targets,
            int count,
            int parameterHash)
        {
            for (int index = 0; index < count; index++)
            {
                if (targets[index].ParameterHash == parameterHash)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Contains(
            AnimTriggerTarget[] targets,
            int count,
            AnimBindingId id)
        {
            for (int index = 0; index < count; index++)
            {
                if (targets[index].BindingId == id)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsTriggerHash(
            AnimTriggerTarget[] targets,
            int count,
            int parameterHash)
        {
            for (int index = 0; index < count; index++)
            {
                if (targets[index].ParameterHash == parameterHash)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Contains(int[] values, int count, int value)
        {
            for (int index = 0; index < count; index++)
            {
                if (values[index] == value)
                {
                    return true;
                }
            }

            return false;
        }

        private static CoCoDiagnostic Error(string message)
        {
            return CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Operator,
                CoCoDiagnosticCode.InvalidOperatorDescriptor,
                message);
        }
    }
}
