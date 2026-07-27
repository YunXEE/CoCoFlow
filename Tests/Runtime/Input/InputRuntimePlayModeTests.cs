using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CoCoFlow.Runtime.Modules.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CoCoFlow.Tests.Runtime.Input
{
    public sealed class InputRuntimePlayModeTests : InputTestFixture
    {
        [UnityTest]
        public IEnumerator RuntimeUsesPlayerInputsActionAssetWithoutCloning()
        {
            var actions = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = new InputActionMap("Gameplay");
            map.AddAction("Move", InputActionType.Value, "<Keyboard>/w");
            actions.AddActionMap(map);

            var gameObject = new GameObject("InputRuntimeTest");
            var playerInput = gameObject.AddComponent<PlayerInput>();
            playerInput.actions = actions;
            var runtime = gameObject.AddComponent<InputRuntime>();

            yield return null;

            Assert.AreSame(playerInput, runtime.PlayerInput);
            Assert.AreSame(playerInput.actions, runtime.Actions);

            Object.Destroy(gameObject);
            Object.Destroy(actions);
        }

        [UnityTest]
        public IEnumerator FencePublishesOnceAndClearsLegacySnapshots()
        {
            var gameObject = new GameObject("InputFenceTest");
            var playerInput = gameObject.AddComponent<PlayerInput>();
            playerInput.actions = ScriptableObject.CreateInstance<InputActionAsset>();
            var runtime = gameObject.AddComponent<InputRuntime>();
            int fenceCount = 0;
            runtime.InputFenced += () => fenceCount++;

            runtime.FenceInput();
            yield return null;

            Assert.AreEqual(1, fenceCount);
            Assert.AreEqual(Vector2.zero, runtime.MoveInput);
            Assert.AreEqual(Vector2.zero, runtime.LookInput);
            Assert.AreEqual(Vector2.zero, runtime.ZoomInput);

            Object.Destroy(playerInput.actions);
            Object.Destroy(gameObject);
        }

        [UnityTest]
        public IEnumerator ActionMapSwitchClearsOldCanceledBeforeReturning()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            var actions = ScriptableObject.CreateInstance<InputActionAsset>();
            var gameplay = new InputActionMap("Gameplay");
            InputAction submit = gameplay.AddAction(
                "Submit",
                InputActionType.Button,
                "<Keyboard>/space");
            var menu = new InputActionMap("Menu");
            menu.AddAction(
                "Confirm",
                InputActionType.Button,
                "<Keyboard>/enter");
            actions.AddActionMap(gameplay);
            actions.AddActionMap(menu);

            var gameObject = new GameObject("ActionMapFenceTest");
            gameObject.SetActive(false);
            var playerInput = gameObject.AddComponent<PlayerInput>();
            playerInput.actions = actions;
            playerInput.defaultActionMap = gameplay.name;
            var runtime = gameObject.AddComponent<InputRuntime>();
            var buffered = new List<InputActionEvent>();
            runtime.ActionChanged += buffered.Add;
            runtime.InputFenced += buffered.Clear;
            gameObject.SetActive(true);
            yield return null;

            Assert.IsTrue(runtime.TryResolveAction(
                submit.id,
                out InputAction runtimeSubmit));
            Assert.IsTrue(runtimeSubmit.enabled);
            Press(keyboard.spaceKey);
            InputSystem.Update();
            Assert.IsNotEmpty(buffered);

            runtime.SwitchActionMap(menu);

            Assert.IsEmpty(buffered);
            Release(keyboard.spaceKey);
            InputSystem.Update();
            Assert.IsEmpty(buffered);

            Object.Destroy(gameObject);
            Object.Destroy(actions);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ManualDisableAndHeldEnableRequireNeutralInput()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            InputActionAsset actions = CreateButtonAsset(
                "Submit",
                "<Keyboard>/space",
                out InputAction authoredAction);
            var gameObject = new GameObject("ManualActionFenceTest");
            gameObject.SetActive(false);
            var playerInput = gameObject.AddComponent<PlayerInput>();
            playerInput.actions = actions;
            var runtime = gameObject.AddComponent<InputRuntime>();
            var buffered = new List<InputActionEvent>();
            runtime.ActionChanged += buffered.Add;
            runtime.InputFenced += buffered.Clear;
            gameObject.SetActive(true);
            yield return null;

            Assert.IsTrue(runtime.TryResolveAction(
                authoredAction.id,
                out InputAction action));
            action.Enable();
            Press(keyboard.spaceKey);
            InputSystem.Update();
            Assert.IsNotEmpty(buffered);

            action.Disable();
            Assert.IsEmpty(buffered);
            action.Enable();
            InputSystem.Update();
            Assert.IsEmpty(buffered);

            Release(keyboard.spaceKey);
            InputSystem.Update();
            yield return null;
            Press(keyboard.spaceKey);
            InputSystem.Update();
            Assert.That(
                buffered.Exists(inputEvent =>
                    inputEvent.Phase == InputActionPhase.Performed),
                Is.True);

            Release(keyboard.spaceKey);
            InputSystem.Update();
            Object.Destroy(gameObject);
            Object.Destroy(actions);
            yield return null;
        }

        [UnityTest]
        public IEnumerator WarmedNeutralGatePollingAllocatesNoManagedMemory()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            InputActionAsset actions = CreateButtonAsset(
                "Submit",
                "<Keyboard>/space",
                out InputAction authoredAction);
            var gameObject = new GameObject("NeutralGateAllocationTest");
            gameObject.SetActive(false);
            var playerInput = gameObject.AddComponent<PlayerInput>();
            playerInput.actions = actions;
            var runtime = gameObject.AddComponent<InputRuntime>();
            gameObject.SetActive(true);
            yield return null;

            Assert.IsTrue(runtime.TryResolveAction(
                authoredAction.id,
                out InputAction action));
            action.Enable();
            Press(keyboard.spaceKey);
            InputSystem.Update();
            runtime.DisableActionForTransition(action);
            runtime.RestoreActionAfterTransition(action, true);
            for (int index = 0; index < 16; index++)
            {
                runtime.ReleaseNeutralActions();
            }

            _ = GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 1000; index++)
            {
                runtime.ReleaseNeutralActions();
            }

            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.AreEqual(before, after);

            Release(keyboard.spaceKey);
            InputSystem.Update();
            Object.Destroy(gameObject);
            Object.Destroy(actions);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlayerInputControlSchemeAndDeviceRemainPresentationAuthority()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
            var actions = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = new InputActionMap("Gameplay");
            InputAction submit = map.AddAction(
                "Submit",
                InputActionType.Button);
            submit.AddBinding("<Keyboard>/space").WithGroup("Keyboard");
            submit.AddBinding("<Gamepad>/buttonSouth").WithGroup("Gamepad");
            actions.AddActionMap(map);
            actions.AddControlScheme("Keyboard")
                .WithRequiredDevice("<Keyboard>");
            actions.AddControlScheme("Gamepad")
                .WithRequiredDevice("<Gamepad>");
            InputActionReference reference =
                InputActionReference.Create(submit);

            var gameObject = new GameObject("InputAuthorityTest");
            gameObject.SetActive(false);
            var playerInput = gameObject.AddComponent<PlayerInput>();
            playerInput.actions = actions;
            var runtime = gameObject.AddComponent<InputRuntime>();
            gameObject.SetActive(true);
            yield return null;

            playerInput.SwitchCurrentControlScheme("Gamepad", gamepad);
            yield return null;
            Assert.AreEqual("Gamepad", runtime.CurrentControlScheme);
            Assert.AreEqual("Gamepad", runtime.CurrentDeviceLayout);
            Assert.IsTrue(runtime.TryGetPrompt(
                reference,
                out InputPromptSnapshot gamepadPrompt));
            Assert.AreEqual(1, gamepadPrompt.BindingIndex);
            StringAssert.Contains(
                "button",
                gamepadPrompt.ControlPath.ToLowerInvariant());

            playerInput.SwitchCurrentControlScheme("Keyboard", keyboard);
            yield return null;
            Assert.AreEqual("Keyboard", runtime.CurrentControlScheme);
            Assert.AreEqual("Keyboard", runtime.CurrentDeviceLayout);
            Assert.IsTrue(runtime.TryGetPrompt(
                reference,
                out InputPromptSnapshot keyboardPrompt));
            Assert.AreEqual(0, keyboardPrompt.BindingIndex);
            StringAssert.Contains(
                "space",
                keyboardPrompt.ControlPath.ToLowerInvariant());

            Object.Destroy(gameObject);
            Object.Destroy(reference);
            Object.Destroy(actions);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RebindCommitPersistsAndStorageFailureRestoresPreviousOverride()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            var actions = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = new InputActionMap("Gameplay");
            InputAction submit = map.AddAction(
                "Submit",
                InputActionType.Button,
                "<Keyboard>/space");
            actions.AddActionMap(map);
            InputActionReference reference =
                InputActionReference.Create(submit);

            var gameObject = new GameObject("InputRebindTest");
            gameObject.SetActive(false);
            var playerInput = gameObject.AddComponent<PlayerInput>();
            playerInput.actions = actions;
            var store = gameObject.AddComponent<TestOverrideStore>();
            var runtime = gameObject.AddComponent<InputRuntime>();
            var controller = gameObject.AddComponent<InputRebindController>();
            SetField(runtime, "bindingOverrideStore", store);
            SetField(controller, "inputRuntime", runtime);
            gameObject.SetActive(true);
            yield return null;

            Assert.IsTrue(runtime.TryResolveAction(
                submit.id,
                out InputAction runtimeAction));
            runtimeAction.Enable();
            System.Guid bindingId = runtimeAction.bindings[0].id;
            int promptChangeCount = 0;
            runtime.PromptChanged += () => promptChangeCount++;

            Assert.IsTrue(controller.TryBegin(
                submit.id,
                bindingId,
                out string beginError),
                beginError);
            Press(keyboard.enterKey);
            InputSystem.Update();
            currentTime += 0.1;
            InputSystem.Update();
            yield return null;
            Release(keyboard.enterKey);
            InputSystem.Update();

            Assert.IsFalse(controller.IsRebinding);
            Assert.AreEqual(1, store.SaveCount);
            Assert.Greater(promptChangeCount, 0);
            StringAssert.Contains(
                "enter",
                runtimeAction.bindings[0].effectivePath.ToLowerInvariant());
            Assert.IsTrue(runtime.TryGetPrompt(
                reference,
                out InputPromptSnapshot prompt));
            StringAssert.Contains(
                "enter",
                prompt.BindingDisplay.ToLowerInvariant());
            string committedPath = runtimeAction.bindings[0].effectivePath;
            int committedSaveCount = store.SaveCount;

            Assert.IsTrue(controller.TryBegin(
                submit.id,
                bindingId,
                out beginError),
                beginError);
            controller.Cancel();

            Assert.IsFalse(controller.IsRebinding);
            Assert.AreEqual(
                committedPath,
                runtimeAction.bindings[0].effectivePath);
            Assert.AreEqual(committedSaveCount, store.SaveCount);

            Assert.IsFalse(controller.TryBegin(
                submit.id,
                System.Guid.NewGuid(),
                out beginError));
            Assert.IsNotEmpty(beginError);
            Assert.AreEqual(
                committedPath,
                runtimeAction.bindings[0].effectivePath);

            store.FailSave = true;
            Assert.IsTrue(controller.TryBegin(
                submit.id,
                bindingId,
                out beginError),
                beginError);
            Press(keyboard.tabKey);
            InputSystem.Update();
            currentTime += 0.1;
            InputSystem.Update();
            yield return null;
            Release(keyboard.tabKey);
            InputSystem.Update();

            Assert.IsFalse(controller.IsRebinding);
            Assert.IsNotEmpty(controller.LastError);
            Assert.AreEqual(
                committedPath,
                runtimeAction.bindings[0].effectivePath);

            Object.Destroy(gameObject);
            Object.Destroy(reference);
            Object.Destroy(actions);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RebindTransitionsDoNotLeakAndHeldControlMustNeutralize()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            InputActionAsset actions = CreateButtonAsset(
                "Submit",
                "<Keyboard>/space",
                out InputAction authoredAction);
            var gameObject = new GameObject("RebindFenceTest");
            gameObject.SetActive(false);
            var playerInput = gameObject.AddComponent<PlayerInput>();
            playerInput.actions = actions;
            var store = gameObject.AddComponent<TestOverrideStore>();
            var runtime = gameObject.AddComponent<InputRuntime>();
            var controller = gameObject.AddComponent<InputRebindController>();
            SetField(runtime, "bindingOverrideStore", store);
            SetField(controller, "inputRuntime", runtime);
            var buffered = new List<InputActionEvent>();
            runtime.ActionChanged += buffered.Add;
            runtime.InputFenced += buffered.Clear;
            gameObject.SetActive(true);
            yield return null;

            Assert.IsTrue(runtime.TryResolveAction(
                authoredAction.id,
                out InputAction action));
            action.Enable();
            Guid bindingId = action.bindings[0].id;

            Press(keyboard.spaceKey);
            InputSystem.Update();
            Assert.IsNotEmpty(buffered);
            Assert.IsTrue(controller.TryBegin(
                action.id,
                bindingId,
                out string error),
                error);
            Assert.IsEmpty(buffered);

            controller.Cancel();
            InputSystem.Update();
            Assert.IsEmpty(buffered);
            Release(keyboard.spaceKey);
            InputSystem.Update();
            yield return null;
            Press(keyboard.spaceKey);
            InputSystem.Update();
            Assert.That(
                buffered.Exists(inputEvent =>
                    inputEvent.Phase == InputActionPhase.Performed),
                Is.True);
            Release(keyboard.spaceKey);
            InputSystem.Update();
            buffered.Clear();

            Assert.IsTrue(controller.TryBegin(
                action.id,
                bindingId,
                out error),
                error);
            Press(keyboard.enterKey);
            InputSystem.Update();
            currentTime += 0.1;
            InputSystem.Update();
            Assert.IsFalse(controller.IsRebinding);
            Assert.IsEmpty(buffered);
            Release(keyboard.enterKey);
            InputSystem.Update();
            yield return null;
            Press(keyboard.enterKey);
            InputSystem.Update();
            Assert.That(
                buffered.Exists(inputEvent =>
                    inputEvent.Phase == InputActionPhase.Performed),
                Is.True);
            Release(keyboard.enterKey);
            InputSystem.Update();
            buffered.Clear();

            store.FailSave = true;
            Assert.IsTrue(controller.TryBegin(
                action.id,
                bindingId,
                out error),
                error);
            Press(keyboard.tabKey);
            InputSystem.Update();
            currentTime += 0.1;
            InputSystem.Update();
            Assert.IsFalse(controller.IsRebinding);
            Assert.IsEmpty(buffered);
            Release(keyboard.tabKey);
            InputSystem.Update();
            yield return null;

            Object.Destroy(gameObject);
            Object.Destroy(actions);
            yield return null;
        }

        [UnityTest]
        public IEnumerator LastUsedDeviceSelectsBindingWithinOneControlScheme()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            var actions = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = new InputActionMap("Gameplay");
            InputAction submit = map.AddAction(
                "Submit",
                InputActionType.Button);
            submit.AddBinding("<Keyboard>/space")
                .WithGroup("KeyboardMouse");
            submit.AddBinding("<Mouse>/leftButton")
                .WithGroup("KeyboardMouse");
            actions.AddActionMap(map);
            actions.AddControlScheme("KeyboardMouse")
                .WithRequiredDevice("<Keyboard>")
                .WithRequiredDevice("<Mouse>");
            InputActionReference reference =
                InputActionReference.Create(submit);

            var gameObject = new GameObject("LastUsedDeviceTest");
            gameObject.SetActive(false);
            var playerInput = gameObject.AddComponent<PlayerInput>();
            playerInput.actions = actions;
            var runtime = gameObject.AddComponent<InputRuntime>();
            gameObject.SetActive(true);
            yield return null;

            playerInput.SwitchCurrentControlScheme(
                "KeyboardMouse",
                keyboard,
                mouse);
            Assert.IsTrue(runtime.TryResolveAction(
                submit.id,
                out InputAction runtimeAction));
            runtimeAction.Enable();

            Press(keyboard.spaceKey);
            InputSystem.Update();
            Release(keyboard.spaceKey);
            InputSystem.Update();
            Assert.AreEqual("Keyboard", runtime.CurrentDeviceLayout);
            Assert.IsTrue(runtime.TryGetPrompt(
                reference,
                out InputPromptSnapshot keyboardPrompt));
            Assert.AreEqual(0, keyboardPrompt.BindingIndex);

            Press(mouse.leftButton);
            InputSystem.Update();
            Release(mouse.leftButton);
            InputSystem.Update();
            Assert.AreEqual("Mouse", runtime.CurrentDeviceLayout);
            Assert.IsTrue(runtime.TryGetPrompt(
                reference,
                out InputPromptSnapshot mousePrompt));
            Assert.AreEqual(1, mousePrompt.BindingIndex);

            Object.Destroy(gameObject);
            Object.Destroy(reference);
            Object.Destroy(actions);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReplacingPlayerInputActionsReconcilesExactSubscriptions()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            InputActionAsset first = CreateButtonAsset(
                "First",
                "<Keyboard>/a",
                out InputAction firstAuthored);
            InputActionAsset second = CreateButtonAsset(
                "Second",
                "<Keyboard>/b",
                out InputAction secondAuthored);

            var gameObject = new GameObject("ActionAssetReplacementTest");
            gameObject.SetActive(false);
            var playerInput = gameObject.AddComponent<PlayerInput>();
            playerInput.actions = first;
            var runtime = gameObject.AddComponent<InputRuntime>();
            gameObject.SetActive(true);
            yield return null;

            int firstEvents = 0;
            int secondEvents = 0;
            runtime.ActionChanged += actionEvent =>
            {
                if (actionEvent.Action.name == "First")
                {
                    firstEvents++;
                }
                else if (actionEvent.Action.name == "Second")
                {
                    secondEvents++;
                }
            };

            Assert.IsTrue(runtime.TryResolveAction(
                firstAuthored.id,
                out InputAction firstRuntimeAction));
            firstRuntimeAction.Enable();
            Press(keyboard.aKey);
            InputSystem.Update();
            Release(keyboard.aKey);
            InputSystem.Update();
            Assert.Greater(firstEvents, 0);

            playerInput.actions = second;
            yield return null;
            Assert.IsTrue(runtime.TryResolveAction(
                secondAuthored.id,
                out InputAction secondRuntimeAction));
            firstRuntimeAction.Enable();
            secondRuntimeAction.Enable();
            int firstBeforeOldAssetInput = firstEvents;

            Press(keyboard.aKey);
            InputSystem.Update();
            Release(keyboard.aKey);
            InputSystem.Update();
            Assert.AreEqual(firstBeforeOldAssetInput, firstEvents);

            Press(keyboard.bKey);
            InputSystem.Update();
            Release(keyboard.bKey);
            InputSystem.Update();
            Assert.Greater(secondEvents, 0);

            runtime.enabled = false;
            int secondBeforeDisableInput = secondEvents;
            Press(keyboard.bKey);
            InputSystem.Update();
            Release(keyboard.bKey);
            InputSystem.Update();
            Assert.AreEqual(secondBeforeDisableInput, secondEvents);

            Object.Destroy(gameObject);
            Object.Destroy(first);
            Object.Destroy(second);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReplacingActiveAssetFencesSynchronouslyAndGatesHeldControl()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            InputActionAsset first = CreateButtonAsset(
                "First",
                "<Keyboard>/a",
                out InputAction firstAuthored);
            InputActionAsset second = CreateButtonAsset(
                "Second",
                "<Keyboard>/a",
                out InputAction secondAuthored);
            var gameObject = new GameObject("ActionAssetFenceTest");
            gameObject.SetActive(false);
            var playerInput = gameObject.AddComponent<PlayerInput>();
            playerInput.defaultActionMap = "Gameplay";
            playerInput.actions = first;
            var runtime = gameObject.AddComponent<InputRuntime>();
            var buffered = new List<InputActionEvent>();
            runtime.ActionChanged += buffered.Add;
            runtime.InputFenced += buffered.Clear;
            gameObject.SetActive(true);
            yield return null;

            Assert.IsTrue(runtime.TryResolveAction(
                firstAuthored.id,
                out InputAction firstAction));
            Assert.IsTrue(firstAction.enabled);
            Press(keyboard.aKey);
            InputSystem.Update();
            Assert.IsNotEmpty(buffered);

            playerInput.actions = second;

            Assert.IsEmpty(buffered);
            InputSystem.Update();
            Assert.IsEmpty(buffered);
            yield return null;
            Assert.IsTrue(runtime.TryResolveAction(
                secondAuthored.id,
                out InputAction secondAction));
            Assert.IsTrue(secondAction.enabled);
            Assert.IsEmpty(buffered);

            Release(keyboard.aKey);
            InputSystem.Update();
            yield return null;
            Press(keyboard.aKey);
            InputSystem.Update();
            Assert.That(
                buffered.Exists(inputEvent =>
                    inputEvent.Action.name == "Second" &&
                    inputEvent.Phase == InputActionPhase.Performed),
                Is.True);

            Release(keyboard.aKey);
            InputSystem.Update();
            Object.Destroy(gameObject);
            Object.Destroy(first);
            Object.Destroy(second);
            yield return null;
        }

        private static InputActionAsset CreateButtonAsset(
            string actionName,
            string binding,
            out InputAction action)
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = new InputActionMap("Gameplay");
            action = map.AddAction(
                actionName,
                InputActionType.Button,
                binding);
            asset.AddActionMap(map);
            return asset;
        }

        private static void SetField(
            object target,
            string name,
            object value)
        {
            FieldInfo field = target.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, name);
            field.SetValue(target, value);
        }

        private sealed class TestOverrideStore :
            MonoBehaviour,
            IInputBindingOverrideStore
        {
            public bool FailSave { get; set; }
            public int SaveCount { get; private set; }
            private string _json = string.Empty;

            public bool TryLoad(string storageKey, out string overrideJson)
            {
                overrideJson = _json;
                return true;
            }

            public bool TrySave(string storageKey, string overrideJson)
            {
                SaveCount++;
                if (FailSave)
                {
                    return false;
                }

                _json = overrideJson;
                return true;
            }
        }
    }
}
