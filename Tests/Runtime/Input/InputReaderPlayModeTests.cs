using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using CoCoFlow.Runtime.Modules.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CoCoFlow.Tests.Runtime.Input
{
    public sealed class InputReaderPlayModeTests : InputTestFixture
    {
        [UnityTest]
        public IEnumerator RuntimeUsesPlayerInputsActionAssetWithoutCloning()
        {
            var actions = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = new InputActionMap("Gameplay");
            map.AddAction("Move", InputActionType.Value, "<Keyboard>/w");
            actions.AddActionMap(map);

            var gameObject = new GameObject("InputReaderTest");
            var playerInput = gameObject.AddComponent<PlayerInput>();
            playerInput.actions = actions;
            var runtime = gameObject.AddComponent<InputReader>();

            yield return null;

            Assert.AreSame(playerInput, runtime.PlayerInput);
            Assert.AreSame(playerInput.actions, runtime.Actions);

            Object.Destroy(gameObject);
            Object.Destroy(actions);
        }

        [UnityTest]
        public IEnumerator ActionEventsPublishAuthoritativeFirstAndIsolateSubscribers()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            InputActionAsset actions = CreateButtonAsset(
                "Submit",
                "<Keyboard>/space",
                out InputAction authoredAction);
            var gameObject = new GameObject("ActionObserverIsolationTest");
            gameObject.SetActive(false);
            var playerInput = gameObject.AddComponent<PlayerInput>();
            playerInput.actions = actions;
            var reader = gameObject.AddComponent<InputReader>();
            var observed = new List<string>();

            reader.ActionChanged += actionEvent =>
            {
                observed.Add($"action:{actionEvent.Phase}:throw");
                throw new InvalidOperationException(
                    $"Expected {actionEvent.Phase} authoritative failure.");
            };
            reader.ActionChanged += actionEvent =>
                observed.Add($"action:{actionEvent.Phase}:follow");
            reader.OnActionPerformed += _ =>
            {
                observed.Add("performed:throw");
                throw new InvalidOperationException(
                    "Expected performed convenience failure.");
            };
            reader.OnActionPerformed += _ => observed.Add("performed:follow");
            reader.OnActionCanceled += _ =>
            {
                observed.Add("canceled:throw");
                throw new InvalidOperationException(
                    "Expected canceled convenience failure.");
            };
            reader.OnActionCanceled += _ => observed.Add("canceled:follow");

            gameObject.SetActive(true);
            yield return null;

            Assert.IsTrue(reader.TryResolveAction(
                authoredAction.id,
                out InputAction action));
            action.Enable();
            LogAssert.Expect(
                LogType.Exception,
                new Regex("Expected Performed authoritative failure"));
            LogAssert.Expect(
                LogType.Exception,
                new Regex("Expected performed convenience failure"));
            LogAssert.Expect(
                LogType.Exception,
                new Regex("Expected Canceled authoritative failure"));
            LogAssert.Expect(
                LogType.Exception,
                new Regex("Expected canceled convenience failure"));

            Press(keyboard.spaceKey);
            InputSystem.Update();
            Release(keyboard.spaceKey);
            InputSystem.Update();

            CollectionAssert.AreEqual(
                new[]
                {
                    "action:Performed:throw",
                    "action:Performed:follow",
                    "performed:throw",
                    "performed:follow",
                    "action:Canceled:throw",
                    "action:Canceled:follow",
                    "canceled:throw",
                    "canceled:follow"
                },
                observed);

            Object.Destroy(gameObject);
            Object.Destroy(actions);
            yield return null;
        }

        [UnityTest]
        public IEnumerator BufferedActionConsumesOnceAndExpires()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            InputActionAsset actions = CreateButtonAsset(
                "Submit",
                "<Keyboard>/space",
                out InputAction authoredAction);
            var gameObject = new GameObject("BufferedActionTest");
            gameObject.SetActive(false);
            var playerInput = gameObject.AddComponent<PlayerInput>();
            playerInput.actions = actions;
            var reader = gameObject.AddComponent<InputReader>();
            SetField(reader, "inputBufferTime", 0f);
            gameObject.SetActive(true);
            yield return null;

            Assert.IsTrue(reader.TryResolveAction(
                authoredAction.id,
                out InputAction action));
            action.Enable();
            Press(keyboard.spaceKey);
            InputSystem.Update();
            Assert.IsTrue(reader.TryConsumeBufferedAction("Submit"));
            Assert.IsFalse(reader.TryConsumeBufferedAction("Submit"));

            Release(keyboard.spaceKey);
            InputSystem.Update();
            Press(keyboard.spaceKey);
            InputSystem.Update();
            yield return null;
            Assert.IsFalse(reader.TryConsumeBufferedAction("Submit"));

            Release(keyboard.spaceKey);
            InputSystem.Update();
            Object.Destroy(gameObject);
            Object.Destroy(actions);
            yield return null;
        }

        [UnityTest]
        public IEnumerator BindingOverridesLoadAfterStoreAwakeExactlyOnce()
        {
            var actions = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = new InputActionMap("Gameplay");
            InputAction submit = map.AddAction(
                "Submit",
                InputActionType.Button,
                "<Keyboard>/space");
            actions.AddActionMap(map);
            submit.ApplyBindingOverride(0, "<Keyboard>/enter");
            string overrideJson = actions.SaveBindingOverridesAsJson();
            actions.RemoveAllBindingOverrides();

            var gameObject = new GameObject("DeferredOverrideLoadTest");
            gameObject.SetActive(false);
            var playerInput = gameObject.AddComponent<PlayerInput>();
            playerInput.actions = actions;
            playerInput.defaultActionMap = map.name;
            var store = gameObject.AddComponent<AwakeInitializedOverrideStore>();
            store.OverrideJson = overrideJson;
            var runtime = gameObject.AddComponent<InputReader>();
            SetField(runtime, "bindingOverrideStore", store);

            gameObject.SetActive(true);
            yield return null;

            Assert.IsTrue(store.AwakeCompleted);
            Assert.AreEqual(0, store.PrematureLoadCount);
            Assert.AreEqual(1, store.LoadCount);
            Assert.IsTrue(runtime.TryResolveAction(
                submit.id,
                out InputAction runtimeSubmit));
            StringAssert.Contains(
                "enter",
                runtimeSubmit.bindings[0].effectivePath.ToLowerInvariant());

            runtime.enabled = false;
            runtime.enabled = true;
            yield return null;
            Assert.AreEqual(1, store.LoadCount);

            Object.Destroy(gameObject);
            Object.Destroy(actions);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RuntimeDoesNotPreemptPerPlayerActionInitialization()
        {
            InputActionAsset actions = CreateButtonAsset(
                "Submit",
                "<Keyboard>/space",
                out _);
            var root = new GameObject("DeferredPlayerInputRoot");
            root.SetActive(false);

            var firstObject = new GameObject("FirstPlayer");
            firstObject.transform.SetParent(root.transform);
            var firstPlayer = firstObject.AddComponent<PlayerInput>();
            firstPlayer.actions = actions;
            firstPlayer.defaultActionMap = "Gameplay";
            var firstRuntime = firstObject.AddComponent<InputReader>();

            var secondObject = new GameObject("SecondPlayer");
            secondObject.transform.SetParent(root.transform);
            var secondPlayer = secondObject.AddComponent<PlayerInput>();
            secondPlayer.actions = actions;
            secondPlayer.defaultActionMap = "Gameplay";
            var secondRuntime = secondObject.AddComponent<InputReader>();

            root.SetActive(true);
            yield return null;

            InputActionAsset firstRuntimeActions = firstRuntime.Actions;
            InputActionAsset secondRuntimeActions = secondRuntime.Actions;
            Assert.AreSame(firstPlayer.actions, firstRuntimeActions);
            Assert.AreSame(secondPlayer.actions, secondRuntimeActions);
            Assert.AreNotSame(firstRuntimeActions, secondRuntimeActions);

            Object.Destroy(root);
            yield return null;
            if (!ReferenceEquals(firstRuntimeActions, actions))
            {
                Object.Destroy(firstRuntimeActions);
            }

            if (!ReferenceEquals(secondRuntimeActions, actions))
            {
                Object.Destroy(secondRuntimeActions);
            }

            Object.Destroy(actions);
            yield return null;
        }

        [UnityTest]
        public IEnumerator HeldContinuousValueStaysZeroUntilNeutralAndNewInput()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            InputActionAsset actions = CreateMoveAsset(out InputAction authoredMove);
            InputActionReference reference =
                InputActionReference.Create(authoredMove);
            var gameObject = new GameObject("ContinuousNeutralGateTest");
            gameObject.SetActive(false);
            var playerInput = gameObject.AddComponent<PlayerInput>();
            playerInput.actions = actions;
            playerInput.defaultActionMap = "Gameplay";
            var runtime = gameObject.AddComponent<InputReader>();
            SetField(runtime, "moveAction", reference);
            gameObject.SetActive(true);
            yield return null;

            Assert.IsTrue(runtime.TryResolveAction(
                authoredMove.id,
                out InputAction move));
            Press(keyboard.wKey);
            InputSystem.Update();
            yield return null;
            Assert.IsTrue(runtime.TryReadValue(
                reference,
                out Vector2 initialValue));
            Assert.Greater(initialValue.y, 0f);
            Assert.Greater(runtime.MoveInput.y, 0f);

            move.Disable();
            move.Enable();
            Assert.IsFalse(runtime.TryReadValue(
                reference,
                out Vector2 gatedValue));
            Assert.AreEqual(Vector2.zero, gatedValue);
            InputSystem.Update();
            yield return null;
            Assert.AreEqual(Vector2.zero, runtime.MoveInput);

            Release(keyboard.wKey);
            InputSystem.Update();
            yield return null;
            Assert.IsTrue(runtime.TryReadValue(
                reference,
                out Vector2 neutralValue));
            Assert.AreEqual(Vector2.zero, neutralValue);
            Assert.AreEqual(Vector2.zero, runtime.MoveInput);

            Press(keyboard.wKey);
            InputSystem.Update();
            yield return null;
            Assert.IsTrue(runtime.TryReadValue(
                reference,
                out Vector2 resumedValue));
            Assert.Greater(resumedValue.y, 0f);
            Assert.Greater(runtime.MoveInput.y, 0f);

            runtime.enabled = false;
            Assert.IsFalse(runtime.TryReadValue(
                reference,
                out Vector2 disabledValue));
            Assert.AreEqual(Vector2.zero, disabledValue);
            Assert.AreEqual(Vector2.zero, runtime.MoveInput);

            Release(keyboard.wKey);
            InputSystem.Update();
            Object.Destroy(gameObject);
            Object.Destroy(reference);
            Object.Destroy(actions);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FencePublishesOnceAndClearsConvenienceValues()
        {
            var gameObject = new GameObject("InputFenceTest");
            var playerInput = gameObject.AddComponent<PlayerInput>();
            playerInput.actions = ScriptableObject.CreateInstance<InputActionAsset>();
            var runtime = gameObject.AddComponent<InputReader>();
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
            var runtime = gameObject.AddComponent<InputReader>();
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
            InputActionReference reference =
                InputActionReference.Create(authoredAction);
            var gameObject = new GameObject("ManualActionFenceTest");
            gameObject.SetActive(false);
            var playerInput = gameObject.AddComponent<PlayerInput>();
            playerInput.actions = actions;
            var runtime = gameObject.AddComponent<InputReader>();
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
            Assert.IsFalse(runtime.TryReadValue(
                reference,
                out float gatedValue));
            Assert.AreEqual(0f, gatedValue);

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
            Object.Destroy(reference);
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
            var runtime = gameObject.AddComponent<InputReader>();
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
            var runtime = gameObject.AddComponent<InputReader>();
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
            var runtime = gameObject.AddComponent<InputReader>();
            var controller = gameObject.AddComponent<InputRebindController>();
            SetField(runtime, "bindingOverrideStore", store);
            SetField(controller, "inputReader", runtime);
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
            InputActionReference reference =
                InputActionReference.Create(authoredAction);
            var gameObject = new GameObject("RebindFenceTest");
            gameObject.SetActive(false);
            var playerInput = gameObject.AddComponent<PlayerInput>();
            playerInput.actions = actions;
            var store = gameObject.AddComponent<TestOverrideStore>();
            var runtime = gameObject.AddComponent<InputReader>();
            var controller = gameObject.AddComponent<InputRebindController>();
            SetField(runtime, "bindingOverrideStore", store);
            SetField(controller, "inputReader", runtime);
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
            Assert.IsFalse(runtime.TryReadValue(
                reference,
                out float canceledHeldValue));
            Assert.AreEqual(0f, canceledHeldValue);
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
            Object.Destroy(reference);
            Object.Destroy(actions);
            yield return null;
        }

        [UnityTest]
        public IEnumerator BoundarySubscriberFailuresDoNotAbortRebindCommit()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            InputActionAsset actions = CreateButtonAsset(
                "Submit",
                "<Keyboard>/space",
                out InputAction authoredAction);
            var gameObject = new GameObject("RebindObserverIsolationTest");
            gameObject.SetActive(false);
            var playerInput = gameObject.AddComponent<PlayerInput>();
            playerInput.actions = actions;
            var store = gameObject.AddComponent<TestOverrideStore>();
            var runtime = gameObject.AddComponent<InputReader>();
            var controller = gameObject.AddComponent<InputRebindController>();
            SetField(runtime, "bindingOverrideStore", store);
            SetField(controller, "inputReader", runtime);
            gameObject.SetActive(true);
            yield return null;

            Assert.IsTrue(runtime.TryResolveAction(
                authoredAction.id,
                out InputAction action));
            action.Enable();
            Guid bindingId = action.bindings[0].id;
            bool throwFence = false;
            bool throwPrompt = false;
            int fenceFollowerCount = 0;
            int promptFollowerCount = 0;
            bool? completion = null;
            runtime.InputFenced += () =>
            {
                if (!throwFence)
                {
                    return;
                }

                throwFence = false;
                throw new InvalidOperationException(
                    "Expected InputFenced subscriber failure.");
            };
            runtime.InputFenced += () => fenceFollowerCount++;
            runtime.PromptChanged += () =>
            {
                if (!throwPrompt)
                {
                    return;
                }

                throwPrompt = false;
                throw new InvalidOperationException(
                    "Expected PromptChanged subscriber failure.");
            };
            runtime.PromptChanged += () => promptFollowerCount++;
            controller.RebindCompleted += succeeded => completion = succeeded;
            store.AfterSuccessfulSave = () =>
            {
                throwFence = true;
                throwPrompt = true;
            };

            Assert.IsTrue(controller.TryBegin(
                action.id,
                bindingId,
                out string error),
                error);
            LogAssert.Expect(
                LogType.Exception,
                new Regex("Expected InputFenced subscriber failure"));
            LogAssert.Expect(
                LogType.Exception,
                new Regex("Expected PromptChanged subscriber failure"));
            Press(keyboard.enterKey);
            InputSystem.Update();
            currentTime += 0.1;
            InputSystem.Update();
            yield return null;
            Release(keyboard.enterKey);
            InputSystem.Update();

            Assert.IsFalse(controller.IsRebinding);
            Assert.IsEmpty(controller.LastError);
            Assert.AreEqual(true, completion);
            Assert.Greater(fenceFollowerCount, 0);
            Assert.Greater(promptFollowerCount, 0);
            StringAssert.Contains(
                "enter",
                action.bindings[0].effectivePath.ToLowerInvariant());
            Assert.AreEqual(
                runtime.CaptureBindingOverrides(),
                store.StoredJson);

            Object.Destroy(gameObject);
            Object.Destroy(actions);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ExternalBindingOverridesFenceRefreshAndRequireNeutralInput()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            InputActionAsset actions = CreateButtonAsset(
                "Submit",
                "<Keyboard>/space",
                out InputAction authoredAction);
            InputActionReference reference =
                InputActionReference.Create(authoredAction);
            var gameObject = new GameObject("ExternalBindingOverrideTest");
            gameObject.SetActive(false);
            var playerInput = gameObject.AddComponent<PlayerInput>();
            playerInput.actions = actions;
            var store = gameObject.AddComponent<TestOverrideStore>();
            var runtime = gameObject.AddComponent<InputReader>();
            SetField(runtime, "bindingOverrideStore", store);
            var buffered = new List<InputActionEvent>();
            int fenceCount = 0;
            int promptCount = 0;
            runtime.ActionChanged += buffered.Add;
            runtime.InputFenced += () =>
            {
                fenceCount++;
                buffered.Clear();
            };
            runtime.PromptChanged += () => promptCount++;
            gameObject.SetActive(true);
            yield return null;

            Assert.IsTrue(runtime.TryResolveAction(
                authoredAction.id,
                out InputAction action));
            action.Enable();
            buffered.Clear();
            fenceCount = 0;
            promptCount = 0;

            Press(keyboard.spaceKey);
            InputSystem.Update();
            Assert.IsNotEmpty(buffered);
            Press(keyboard.enterKey);
            InputSystem.Update();

            action.ApplyBindingOverride(0, "<Keyboard>/enter");

            Assert.IsEmpty(buffered);
            Assert.IsFalse(runtime.TryReadValue(
                reference,
                out float overriddenHeldValue));
            Assert.AreEqual(0f, overriddenHeldValue);
            Assert.GreaterOrEqual(fenceCount, 2);
            Assert.Greater(promptCount, 0);
            Assert.AreEqual(0, store.SaveCount);
            Assert.IsTrue(runtime.TryGetPrompt(
                reference,
                out InputPromptSnapshot enterPrompt));
            StringAssert.Contains(
                "enter",
                enterPrompt.BindingDisplay.ToLowerInvariant());
            InputSystem.Update();
            Assert.IsEmpty(buffered);

            Release(keyboard.enterKey);
            InputSystem.Update();
            Release(keyboard.spaceKey);
            InputSystem.Update();
            yield return null;
            yield return null;
            Assert.IsTrue(action.enabled);
            Assert.IsTrue(keyboard.enterKey.CheckStateIsAtDefault());
            Assert.AreEqual(InputActionPhase.Waiting, action.phase);
            Press(keyboard.enterKey);
            InputSystem.Update();
            Assert.That(
                buffered.Exists(inputEvent =>
                    inputEvent.Phase == InputActionPhase.Performed),
                Is.True);
            Release(keyboard.enterKey);
            InputSystem.Update();
            buffered.Clear();
            fenceCount = 0;
            promptCount = 0;

            Press(keyboard.spaceKey);
            InputSystem.Update();
            action.RemoveBindingOverride(0);

            Assert.IsEmpty(buffered);
            Assert.GreaterOrEqual(fenceCount, 2);
            Assert.Greater(promptCount, 0);
            Assert.AreEqual(0, store.SaveCount);
            Assert.IsTrue(runtime.TryGetPrompt(
                reference,
                out InputPromptSnapshot spacePrompt));
            StringAssert.Contains(
                "space",
                spacePrompt.BindingDisplay.ToLowerInvariant());
            InputSystem.Update();
            Assert.IsEmpty(buffered);

            Release(keyboard.spaceKey);
            InputSystem.Update();
            yield return null;
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
            Object.Destroy(reference);
            Object.Destroy(actions);
            yield return null;
        }

        [UnityTest]
        public IEnumerator UnpairedBindingChangeNotificationRecoversNextFrame()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            InputActionAsset actions = CreateButtonAsset(
                "Submit",
                "<Keyboard>/space",
                out InputAction authoredAction);
            var gameObject = new GameObject("BindingChangeRecoveryTest");
            gameObject.SetActive(false);
            var playerInput = gameObject.AddComponent<PlayerInput>();
            playerInput.actions = actions;
            var runtime = gameObject.AddComponent<InputReader>();
            var buffered = new List<InputActionEvent>();
            int fenceCount = 0;
            int promptCount = 0;
            runtime.ActionChanged += buffered.Add;
            runtime.InputFenced += () =>
            {
                fenceCount++;
                buffered.Clear();
            };
            runtime.PromptChanged += () => promptCount++;
            gameObject.SetActive(true);
            yield return null;

            Assert.IsTrue(runtime.TryResolveAction(
                authoredAction.id,
                out InputAction action));
            action.Enable();
            buffered.Clear();
            fenceCount = 0;
            promptCount = 0;

            InvokeGlobalActionChange(
                runtime,
                actions,
                InputActionChange.BoundControlsAboutToChange);
            Press(keyboard.spaceKey);
            InputSystem.Update();
            Assert.IsEmpty(buffered);

            Release(keyboard.spaceKey);
            InputSystem.Update();
            yield return null;
            Assert.GreaterOrEqual(fenceCount, 2);
            Assert.Greater(promptCount, 0);

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
            var runtime = gameObject.AddComponent<InputReader>();
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
            var runtime = gameObject.AddComponent<InputReader>();
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
            InputActionReference secondReference =
                InputActionReference.Create(secondAuthored);
            var gameObject = new GameObject("ActionAssetFenceTest");
            gameObject.SetActive(false);
            var playerInput = gameObject.AddComponent<PlayerInput>();
            playerInput.defaultActionMap = "Gameplay";
            playerInput.actions = first;
            var runtime = gameObject.AddComponent<InputReader>();
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
            Assert.IsFalse(runtime.TryReadValue(
                secondReference,
                out float replacementHeldValue));
            Assert.AreEqual(0f, replacementHeldValue);

            Release(keyboard.aKey);
            InputSystem.Update();
            yield return null;
            Press(keyboard.aKey);
            InputSystem.Update();
            Assert.IsTrue(runtime.TryReadValue(
                secondReference,
                out float replacementResumedValue));
            Assert.Greater(replacementResumedValue, 0f);
            Assert.That(
                buffered.Exists(inputEvent =>
                    inputEvent.Action.name == "Second" &&
                    inputEvent.Phase == InputActionPhase.Performed),
                Is.True);

            Release(keyboard.aKey);
            InputSystem.Update();
            Object.Destroy(gameObject);
            Object.Destroy(secondReference);
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

        private static InputActionAsset CreateMoveAsset(out InputAction move)
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = new InputActionMap("Gameplay");
            move = map.AddAction("Move", InputActionType.Value);
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
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

        private static void InvokeGlobalActionChange(
            InputReader runtime,
            object actionOrMap,
            InputActionChange change)
        {
            MethodInfo method = typeof(InputReader).GetMethod(
                "OnGlobalActionChange",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            method.Invoke(runtime, new[] { actionOrMap, (object)change });
        }

        private sealed class TestOverrideStore :
            MonoBehaviour,
            IInputBindingOverrideStore
        {
            public bool FailSave { get; set; }
            public int SaveCount { get; private set; }
            public string StoredJson => _json;
            public System.Action AfterSuccessfulSave { get; set; }
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
                AfterSuccessfulSave?.Invoke();
                return true;
            }
        }

        private sealed class AwakeInitializedOverrideStore :
            MonoBehaviour,
            IInputBindingOverrideStore
        {
            public string OverrideJson { get; set; } = string.Empty;
            public bool AwakeCompleted { get; private set; }
            public int PrematureLoadCount { get; private set; }
            public int LoadCount { get; private set; }

            private void Awake()
            {
                AwakeCompleted = true;
            }

            public bool TryLoad(string storageKey, out string overrideJson)
            {
                LoadCount++;
                if (!AwakeCompleted)
                {
                    PrematureLoadCount++;
                    overrideJson = string.Empty;
                    return false;
                }

                overrideJson = OverrideJson;
                return true;
            }

            public bool TrySave(string storageKey, string overrideJson) =>
                false;
        }
    }
}
