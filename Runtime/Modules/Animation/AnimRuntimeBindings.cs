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

    [Serializable]
    public struct AnimPlaybackLayerBinding
    {
        [SerializeField, Min(0)] private int controllerLayer;

        public int ControllerLayer => controllerLayer;
    }

    [Serializable]
    public struct AnimStateBinding
    {
        [SerializeField] private ulong bindingId;
        [SerializeField, Min(0)] private int controllerLayer;
        [SerializeField] private string fullPath;

        public ulong BindingId => bindingId;
        public int ControllerLayer => controllerLayer;
        public string FullPath => fullPath ?? string.Empty;
    }

    [Serializable]
    public struct AnimModulationBinding
    {
        [SerializeField] private ulong bindingId;
        [SerializeField] private AnimModulationKind modulationKind;
        [SerializeField] private string parameterName;
        [SerializeField, Min(0)] private int controllerLayer;
        [SerializeField] private Transform presentationOffset;

        public ulong BindingId => bindingId;
        public AnimModulationKind ModulationKind => modulationKind;
        public string ParameterName => parameterName ?? string.Empty;
        public int ControllerLayer => controllerLayer;
        public Transform PresentationOffset => presentationOffset;
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

    internal readonly struct AnimStateTarget
    {
        internal AnimStateTarget(
            AnimBindingId bindingId,
            int controllerLayer,
            int stateHash)
        {
            BindingId = bindingId;
            ControllerLayer = controllerLayer;
            StateHash = stateHash;
        }

        internal AnimBindingId BindingId { get; }
        internal int ControllerLayer { get; }
        internal int StateHash { get; }
    }

    internal readonly struct AnimModulationTarget
    {
        internal AnimModulationTarget(
            AnimBindingId bindingId,
            AnimModulationKind kind,
            int parameterHash,
            int controllerLayer,
            Transform presentationOffset)
        {
            BindingId = bindingId;
            Kind = kind;
            ParameterHash = parameterHash;
            ControllerLayer = controllerLayer;
            PresentationOffset = presentationOffset;
        }

        internal AnimBindingId BindingId { get; }
        internal AnimModulationKind Kind { get; }
        internal int ParameterHash { get; }
        internal int ControllerLayer { get; }
        internal Transform PresentationOffset { get; }
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

        internal static bool TryBuildPlayback(
            AnimatorControllerPlayable controller,
            AnimPlaybackLayerBinding[] layerBindings,
            AnimStateBinding[] stateBindings,
            out int[] controllerLayers,
            out AnimStateTarget[] stateTargets,
            out CoCoDiagnostic diagnostic)
        {
            layerBindings ??= Array.Empty<AnimPlaybackLayerBinding>();
            stateBindings ??= Array.Empty<AnimStateBinding>();
            if (!controller.IsValid() ||
                layerBindings.Length == 0 ||
                layerBindings.Length > AnimContractLimits.PlaybackLayerCount)
            {
                controllerLayers = null;
                stateTargets = null;
                diagnostic = Error(
                    "AnimOperator requires one to four Playable layer bindings.");
                return false;
            }

            int layerCount = controller.GetLayerCount();
            controllerLayers = new int[layerBindings.Length];
            for (int index = 0; index < layerBindings.Length; index++)
            {
                int layer = layerBindings[index].ControllerLayer;
                if (layer < 0 || layer >= layerCount ||
                    Contains(controllerLayers, index, layer))
                {
                    controllerLayers = null;
                    stateTargets = null;
                    diagnostic = Error(
                        "Playable layer bindings must reference unique controller layers.");
                    return false;
                }

                controllerLayers[index] = layer;
            }

            stateTargets = new AnimStateTarget[stateBindings.Length];
            for (int index = 0; index < stateBindings.Length; index++)
            {
                AnimStateBinding binding = stateBindings[index];
                if (!AnimBindingId.TryCreate(binding.BindingId, out AnimBindingId bindingId) ||
                    string.IsNullOrWhiteSpace(binding.FullPath) ||
                    Contains(stateTargets, index, bindingId) ||
                    !Contains(controllerLayers, controllerLayers.Length, binding.ControllerLayer))
                {
                    controllerLayers = null;
                    stateTargets = null;
                    diagnostic = Error(
                        "State bindings require unique ids, full paths and one configured controller layer.");
                    return false;
                }

                int stateHash = Animator.StringToHash(binding.FullPath);
                if (!controller.HasState(binding.ControllerLayer, stateHash) ||
                    ContainsStateTarget(
                        stateTargets,
                        index,
                        binding.ControllerLayer,
                        stateHash))
                {
                    controllerLayers = null;
                    stateTargets = null;
                    diagnostic = Error(
                        "State bindings must target unique states present in their Controller layers.");
                    return false;
                }

                stateTargets[index] =
                    new AnimStateTarget(bindingId, binding.ControllerLayer, stateHash);
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal static bool TryBuildModulation(
            Animator animator,
            AnimModulationBinding[] bindings,
            out AnimModulationTarget[] targets,
            out CoCoDiagnostic diagnostic)
        {
            bindings ??= Array.Empty<AnimModulationBinding>();
            if (animator == null || bindings.Length > AnimContractLimits.ModulationLaneCount)
            {
                targets = null;
                diagnostic = Error(
                    "Animation modulation bindings require one Animator and at most eight entries.");
                return false;
            }

            AnimatorControllerParameter[] controllerParameters = animator.parameters;
            int layerCount = animator.layerCount;
            targets = new AnimModulationTarget[bindings.Length];
            for (int index = 0; index < bindings.Length; index++)
            {
                AnimModulationBinding binding = bindings[index];
                if (!AnimBindingId.TryCreate(binding.BindingId, out AnimBindingId bindingId) ||
                    binding.ModulationKind < AnimModulationKind.FloatParameter ||
                    binding.ModulationKind > AnimModulationKind.PresentationOffsetRotation ||
                    Contains(targets, index, bindingId))
                {
                    targets = null;
                    diagnostic = Error(
                        "Modulation bindings require unique ids and supported target kinds.");
                    return false;
                }

                int parameterHash = 0;
                switch (binding.ModulationKind)
                {
                    case AnimModulationKind.FloatParameter:
                        parameterHash = Animator.StringToHash(binding.ParameterName);
                        if (string.IsNullOrWhiteSpace(binding.ParameterName) ||
                            !TryFindParameter(
                                controllerParameters,
                                parameterHash,
                                out AnimatorControllerParameter parameter) ||
                            parameter.type != AnimatorControllerParameterType.Float)
                        {
                            targets = null;
                            diagnostic = Error(
                                "Float modulation must target one Float Animator parameter.");
                            return false;
                        }

                        break;
                    case AnimModulationKind.LayerWeight:
                        if (binding.ControllerLayer < 0 || binding.ControllerLayer >= layerCount)
                        {
                            targets = null;
                            diagnostic = Error(
                                "Layer-weight modulation references an invalid Animator layer.");
                            return false;
                        }

                        break;
                    case AnimModulationKind.PresentationOffsetPosition:
                    case AnimModulationKind.PresentationOffsetRotation:
                        if (binding.PresentationOffset == null)
                        {
                            targets = null;
                            diagnostic = Error(
                                "Presentation-offset modulation requires an explicit Transform.");
                            return false;
                        }

                        break;
                }

                var target = new AnimModulationTarget(
                    bindingId,
                    binding.ModulationKind,
                    parameterHash,
                    binding.ControllerLayer,
                    binding.PresentationOffset);
                if (ContainsModulationTarget(targets, index, target))
                {
                    targets = null;
                    diagnostic = Error(
                        "Modulation bindings must target unique Animator or presentation properties.");
                    return false;
                }

                targets[index] = target;
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

        internal static bool TryFind(
            AnimStateTarget[] targets,
            AnimBindingId bindingId,
            out AnimStateTarget target)
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

        internal static bool TryFind(
            AnimModulationTarget[] targets,
            AnimBindingId bindingId,
            out AnimModulationTarget target)
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

        private static bool Contains(
            AnimStateTarget[] targets,
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

        private static bool ContainsStateTarget(
            AnimStateTarget[] targets,
            int count,
            int controllerLayer,
            int stateHash)
        {
            for (int index = 0; index < count; index++)
            {
                if (targets[index].ControllerLayer == controllerLayer &&
                    targets[index].StateHash == stateHash)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Contains(
            AnimModulationTarget[] targets,
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

        private static bool ContainsModulationTarget(
            AnimModulationTarget[] targets,
            int count,
            in AnimModulationTarget candidate)
        {
            for (int index = 0; index < count; index++)
            {
                AnimModulationTarget current = targets[index];
                if (current.Kind != candidate.Kind)
                {
                    continue;
                }

                switch (candidate.Kind)
                {
                    case AnimModulationKind.FloatParameter:
                        if (current.ParameterHash == candidate.ParameterHash)
                        {
                            return true;
                        }

                        break;
                    case AnimModulationKind.LayerWeight:
                        if (current.ControllerLayer == candidate.ControllerLayer)
                        {
                            return true;
                        }

                        break;
                    case AnimModulationKind.PresentationOffsetPosition:
                    case AnimModulationKind.PresentationOffsetRotation:
                        if (current.PresentationOffset == candidate.PresentationOffset)
                        {
                            return true;
                        }

                        break;
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
