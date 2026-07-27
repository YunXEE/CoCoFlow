using System.Collections;
using System.Reflection;
using CoCoFlow.Runtime.Modules.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

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

            playerInput.SwitchCurrentControlScheme("Keyboard", keyboard);
            yield return null;
            Assert.AreEqual("Keyboard", runtime.CurrentControlScheme);
            Assert.AreEqual("Keyboard", runtime.CurrentDeviceLayout);

            Object.Destroy(gameObject);
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
                0,
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
