using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.Gameplay.Character
{
    public class CharacterInputDriver : MonoBehaviour
    {
        [Header("Context")]
        [SerializeField] private MonoBehaviour contextProvider;

        [Header("Input Source")]
        [SerializeField] private MonoBehaviour inputIntentSource;
        [SerializeField] private bool updateAutomatically = true;

        [Header("Action Names")]
        [SerializeField] private string jumpActionName = "Jump";
        [SerializeField] private string attackActionName = "Attack";
        [SerializeField] private string interactActionName = "Interact";
        [SerializeField] private string useSkillActionName = "UseSkill";

        private CharacterContext _context;
        private ICoCoIntentSource<CoCoInputIntent> _inputIntentSource;
        private int _lastPerformedSequence;

        #region Public API

        public void SetContextProvider(MonoBehaviour provider)
        {
            contextProvider = provider;
            _context = null;
        }

        public void SetInputIntentSource(MonoBehaviour source)
        {
            inputIntentSource = source;
            _inputIntentSource = null;
            _lastPerformedSequence = 0;
        }

        #endregion

        #region Internal Logic

        private CharacterContext Context => ResolveContext();
        private ICoCoIntentSource<CoCoInputIntent> InputIntentSource => ResolveInputIntentSource();

        private void Awake()
        {
            ResolveContext();
            ResolveInputIntentSource();
        }

        private void Update()
        {
            if (updateAutomatically)
            {
                SampleInput();
            }
        }

        private bool SampleInput()
        {
            var targetContext = Context;
            var source = InputIntentSource;
            if (targetContext == null || source?.Intent == null) return false;

            ApplyInputIntent(source.Intent);
            return true;
        }

        private void ApplyInputIntent(CoCoInputIntent inputIntent)
        {
            var targetContext = Context;
            if (targetContext == null || inputIntent == null) return;

            var characterIntent = targetContext.Intent;
            characterIntent.move = inputIntent.move;
            characterIntent.look = inputIntent.look;
            characterIntent.ClearDiscrete();

            if (inputIntent.performedSequence == _lastPerformedSequence)
            {
                return;
            }

            _lastPerformedSequence = inputIntent.performedSequence;

            var performedAction = inputIntent.performedAction;
            if (string.IsNullOrEmpty(performedAction)) return;

            characterIntent.jump = IsAction(performedAction, jumpActionName);
            characterIntent.attack = IsAction(performedAction, attackActionName);
            characterIntent.interact = IsAction(performedAction, interactActionName);
            characterIntent.useSkill = IsAction(performedAction, useSkillActionName);
        }

        private CharacterContext ResolveContext()
        {
            if (_context != null) return _context;

            if (TryGetContextFromProvider(contextProvider, out _context))
            {
                return _context;
            }

            var behaviours = GetComponents<MonoBehaviour>();
            foreach (var behaviour in behaviours)
            {
                if (ReferenceEquals(behaviour, this)) continue;
                if (TryGetContextFromProvider(behaviour, out _context))
                {
                    if (contextProvider == null)
                    {
                        contextProvider = behaviour;
                    }
                    return _context;
                }
            }

            return null;
        }

        private ICoCoIntentSource<CoCoInputIntent> ResolveInputIntentSource()
        {
            if (_inputIntentSource != null) return _inputIntentSource;

            if (inputIntentSource is ICoCoIntentSource<CoCoInputIntent> explicitSource)
            {
                _inputIntentSource = explicitSource;
                return _inputIntentSource;
            }

            if (CoCoServices.TryGet(out ICoCoIntentSource<CoCoInputIntent> serviceSource))
            {
                _inputIntentSource = serviceSource;
                return _inputIntentSource;
            }

            return null;
        }

        private static bool TryGetContextFromProvider(
            object provider,
            out CharacterContext targetContext)
        {
            if (provider is ICoCoContextProvider<CharacterContext> typedProvider)
            {
                targetContext = typedProvider.Context;
                return targetContext != null;
            }

            targetContext = null;
            return false;
        }

        private static bool IsAction(string actionName, string expectedActionName)
        {
            return !string.IsNullOrEmpty(expectedActionName) &&
                   string.Equals(actionName, expectedActionName, System.StringComparison.Ordinal);
        }

        private void OnValidate()
        {
            if (ReferenceEquals(contextProvider, this))
            {
                contextProvider = null;
            }

            if (ReferenceEquals(inputIntentSource, this))
            {
                inputIntentSource = null;
            }
        }

        private void Reset()
        {
            var behaviours = GetComponents<MonoBehaviour>();
            foreach (var behaviour in behaviours)
            {
                if (ReferenceEquals(behaviour, this)) continue;

                if (contextProvider == null &&
                    behaviour is ICoCoContextProvider<CharacterContext>)
                {
                    contextProvider = behaviour;
                }

                if (inputIntentSource == null &&
                    behaviour is ICoCoIntentSource<CoCoInputIntent>)
                {
                    inputIntentSource = behaviour;
                }
            }
        }

        #endregion
    }
}
