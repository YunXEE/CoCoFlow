using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoCoFlow.Runtime.Gameplay.Character
{
    public class CharacterInputDriver :
        MonoBehaviour,
        ICharacterContextSource,
        ICharacterContextSourceUpdateMode
    {
        [Header("Context")]
        [CoCoContextProvider(typeof(CharacterContext))]
        [SerializeField] private MonoBehaviour contextProvider;

        [Header("Input Source")]
        [SerializeField] private InputReader inputReader;
        [SerializeField] private InputActionReference moveAction;
        [SerializeField] private InputActionReference lookAction;
        [SerializeField] private InputActionReference jumpAction;
        [SerializeField] private InputActionReference attackAction;
        [SerializeField] private InputActionReference interactAction;
        [SerializeField] private InputActionReference useSkillAction;
        [SerializeField] private int sourcePriority = 30;
        [SerializeField] private bool updateAutomatically = true;

        private CharacterContext _context;
        private bool _pendingJump;
        private bool _pendingAttack;
        private bool _pendingInteract;
        private bool _pendingUseSkill;
        private bool _isProviderDriven;

        #region Public API

        public int Priority => sourcePriority;
        public bool IsProviderDriven => _isProviderDriven;

        public void WriteToContext(CharacterContext context)
        {
            if (context == null || inputReader == null) return;

            ApplyInput(context);
        }

        public void SetProviderDriven(bool providerDriven)
        {
            _isProviderDriven = providerDriven;
        }

        public void SetContextProvider(MonoBehaviour provider)
        {
            contextProvider = provider;
            _context = null;
        }

        public void SetInputReader(InputReader reader)
        {
            if (ReferenceEquals(inputReader, reader)) return;

            if (isActiveAndEnabled)
            {
                UnsubscribeInput();
            }

            inputReader = reader;
            ClearPendingInput();

            if (isActiveAndEnabled)
            {
                SubscribeInput();
            }
        }

        #endregion

        #region Internal Logic

        private CharacterContext Context => ResolveContext();

        private void Awake()
        {
            ResolveContext();
        }

        private void OnEnable()
        {
            SubscribeInput();
        }

        private void OnDisable()
        {
            UnsubscribeInput();
            ClearPendingInput();
        }

        private void Update()
        {
            if (updateAutomatically && !_isProviderDriven)
            {
                SampleInput();
            }
        }

        private bool SampleInput()
        {
            var targetContext = Context;
            if (targetContext == null || inputReader == null) return false;

            ApplyInput(targetContext);
            return true;
        }

        private void ApplyInput(CharacterContext targetContext)
        {
            if (targetContext == null || inputReader == null) return;

            var characterIntent = targetContext.Intent;
            characterIntent.move = inputReader.TryReadValue(
                moveAction,
                out Vector2 move)
                ? move
                : Vector2.zero;
            characterIntent.look = inputReader.TryReadValue(
                lookAction,
                out Vector2 look)
                ? look
                : Vector2.zero;
            characterIntent.ClearDiscrete();
            characterIntent.jump = _pendingJump;
            characterIntent.attack = _pendingAttack;
            characterIntent.interact = _pendingInteract;
            characterIntent.useSkill = _pendingUseSkill;
            ClearPendingInput();
        }

        private void HandleActionChanged(InputActionEvent actionEvent)
        {
            if (actionEvent.Phase != InputActionPhase.Performed)
            {
                return;
            }

            _pendingJump |= Matches(jumpAction, actionEvent.Action);
            _pendingAttack |= Matches(attackAction, actionEvent.Action);
            _pendingInteract |= Matches(interactAction, actionEvent.Action);
            _pendingUseSkill |= Matches(useSkillAction, actionEvent.Action);
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

        private void SubscribeInput()
        {
            if (inputReader == null)
            {
                return;
            }

            inputReader.ActionChanged += HandleActionChanged;
            inputReader.InputFenced += ClearPendingInput;
        }

        private void UnsubscribeInput()
        {
            if (inputReader == null)
            {
                return;
            }

            inputReader.ActionChanged -= HandleActionChanged;
            inputReader.InputFenced -= ClearPendingInput;
        }

        private void ClearPendingInput()
        {
            _pendingJump = false;
            _pendingAttack = false;
            _pendingInteract = false;
            _pendingUseSkill = false;
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

        private static bool Matches(
            InputActionReference reference,
            InputAction action)
        {
            return reference != null &&
                   reference.action != null &&
                   action != null &&
                   reference.action.id == action.id;
        }

        private void OnValidate()
        {
            if (ReferenceEquals(contextProvider, this))
            {
                contextProvider = null;
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

            }

            inputReader = GetComponent<InputReader>();
        }

        #endregion
    }
}
