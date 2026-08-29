using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Gameplay.Character;
using CoCoFlow.Runtime.Gameplay.Enemy;
using CoCoFlow.Runtime.Gameplay.Item;
using CoCoFlow.Runtime.Modules.Input;
using CoCoFlow.Runtime.Modules.Persistence.Context;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.Splines;
using UnityEngine.TestTools;

namespace CoCoFlow.Tests.Runtime.ContextLifecycle
{
    public class CoCoFlowContextLifecycleTests
    {
        #region Public API


        [TearDown]
        public void TearDown()
        {
            CoCoServices.ClearAll();
        }



        [Test]
        public void CharacterContextSourceSerializedFieldsLiveOnNongenericProviderBase()
        {
            var sourceField = FindPrivateField(typeof(CharacterContextProviderBase), "contextSources");
            var frameField = FindPrivateField(typeof(CharacterContextProviderBase), "resolveSourcesOncePerFrame");

            Assert.IsNotNull(sourceField);
            Assert.IsNotNull(frameField);
            Assert.AreSame(typeof(CharacterContextProviderBase), sourceField.DeclaringType);
            Assert.AreSame(typeof(CharacterContextProviderBase), frameField.DeclaringType);
            Assert.IsFalse(typeof(CharacterContextProviderBase).IsGenericType);
            Assert.IsNull(typeof(CharacterContextProvider<>).GetField(
                "contextSources",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
            Assert.IsTrue(typeof(ICoCoContextFrameResolver).IsAssignableFrom(
                typeof(CharacterContextProviderBase)));
        }

        [Test]
        public void CharacterContextProviderWritesSourcesByPriority()
        {
            var root = new GameObject("Character Context Sources Test");
            try
            {
                var provider = root.AddComponent<CharacterContextProvider>();
                var low = root.AddComponent<TestCharacterContextSource>();
                low.Configure(10, Vector2.right, false);
                var high = root.AddComponent<TestCharacterContextSource>();
                high.Configure(50, Vector2.up, true);

                provider.SetContextSources(new MonoBehaviour[] { high, null, low });
                provider.ResolveContextFrame(provider.Context);

                Assert.AreEqual(Vector2.up, provider.Context.Intent.move);
                Assert.IsTrue(provider.Context.Intent.attack);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CharacterContextProviderIgnoresInactiveOrDisabledSources()
        {
            var root = new GameObject("Character Context Active Source Test");
            var inactiveRoot = new GameObject("Inactive Context Source");
            try
            {
                var provider = root.AddComponent<CharacterContextProvider>();
                var active = root.AddComponent<TestCharacterContextSource>();
                active.Configure(10, Vector2.up, true);

                var inactive = inactiveRoot.AddComponent<TestCharacterContextSource>();
                inactive.Configure(90, Vector2.down, false);
                inactiveRoot.SetActive(false);

                var disabled = root.AddComponent<TestCharacterContextSource>();
                disabled.Configure(100, Vector2.left, false);
                disabled.enabled = false;

                provider.SetContextSources(new MonoBehaviour[] { active, inactive, disabled });
                provider.ResolveContextFrame(provider.Context);

                Assert.AreEqual(Vector2.up, provider.Context.Intent.move);
                Assert.IsTrue(provider.Context.Intent.attack);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(inactiveRoot);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CharacterContextProviderUsesDeclarationOrderForEqualPrioritySources()
        {
            var root = new GameObject("Character Context Equal Priority Test");
            try
            {
                var provider = root.AddComponent<CharacterContextProvider>();
                var first = root.AddComponent<TestCharacterContextSource>();
                first.Configure(10, Vector2.right, false);
                var second = root.AddComponent<TestCharacterContextSource>();
                second.Configure(10, Vector2.up, true);

                provider.SetContextSources(new MonoBehaviour[] { first, second });
                provider.ResolveContextFrame(provider.Context);

                Assert.AreEqual(Vector2.up, provider.Context.Intent.move);
                Assert.IsTrue(provider.Context.Intent.attack);

                provider.Context.Intent.move = Vector2.zero;
                provider.Context.Intent.attack = false;

                provider.SetContextSources(new MonoBehaviour[] { second, first });
                provider.ResolveContextFrame(provider.Context);

                Assert.AreEqual(Vector2.right, provider.Context.Intent.move);
                Assert.IsFalse(provider.Context.Intent.attack);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void SetContextSourcesInvalidatesSameFrameSourceCache()
        {
            var root = new GameObject("Character Context Source Cache Test");
            try
            {
                var provider = root.AddComponent<CharacterContextProvider>();
                var first = root.AddComponent<TestCharacterContextSource>();
                first.Configure(10, Vector2.right, false);
                var second = root.AddComponent<TestCharacterContextSource>();
                second.Configure(10, Vector2.up, true);

                provider.SetContextSources(new MonoBehaviour[] { first });
                provider.ResolveContextFrame(provider.Context);

                Assert.AreEqual(Vector2.right, provider.Context.Intent.move);
                Assert.IsFalse(provider.Context.Intent.attack);

                provider.SetContextSources(new MonoBehaviour[] { second });
                provider.ResolveContextFrame(provider.Context);

                Assert.AreEqual(Vector2.up, provider.Context.Intent.move);
                Assert.IsTrue(provider.Context.Intent.attack);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [UnityTest]
        public IEnumerator ProviderDrivenCharacterInputDriverSkipsAutomaticUpdate()
        {
            var root = new GameObject("Provider Driven Character Input Test");
            CharacterInputFixture input = null;
            try
            {
                var provider = root.AddComponent<CharacterContextProvider>();
                var driver = root.AddComponent<CharacterInputDriver>();
                input = CreateCharacterInputFixture();

                driver.SetContextProvider(provider);
                input.Configure(driver);
                provider.SetContextSources(new MonoBehaviour[] { driver });

                input.Press(input.Gamepad.buttonWest);
                yield return null;
                provider.ResolveContextFrame(provider.Context);

                Assert.IsTrue(driver.IsProviderDriven);
                Assert.IsTrue(provider.Context.Intent.attack);

                yield return null;

                Assert.IsTrue(provider.Context.Intent.attack);

                provider.SetContextSources(null);
                Assert.IsFalse(driver.IsProviderDriven);
            }
            finally
            {
                input?.Destroy();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [UnityTest]
        public IEnumerator ProviderDrivenEnemySplineSkipsAutomaticUpdate()
        {
            var enemy = new GameObject("Provider Driven Enemy Spline Test");
            var splineRoot = new GameObject("Provider Driven Spline Root");
            var config = ScriptableObject.CreateInstance<EnemyConfigData>();

            try
            {
                enemy.transform.position = Vector3.zero;
                var provider = enemy.AddComponent<CharacterContextProvider>();
                var spline = enemy.AddComponent<EnemySpline>();
                var splineContainer = splineRoot.AddComponent<SplineContainer>();
                splineContainer.Spline = new Spline
                {
                    new BezierKnot(new float3(0f, 0f, 0f)),
                    new BezierKnot(new float3(0f, 0f, 10f))
                };

                spline.SetContextProvider(provider);
                spline.SetConfigData(config);
                spline.SetSplineContainer(splineContainer);
                provider.SetContextSources(new MonoBehaviour[] { spline });

                Assert.IsTrue(spline.IsProviderDriven);
                Assert.IsTrue(spline.Tick(provider.Context, 0.25f));
                float progressAfterProviderTick = spline.RouteProgress;
                Assert.Greater(progressAfterProviderTick, 0f);

                yield return null;

                Assert.AreEqual(progressAfterProviderTick, spline.RouteProgress, 0.00001f);

                provider.SetContextSources(null);
                Assert.IsFalse(spline.IsProviderDriven);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
                UnityEngine.Object.DestroyImmediate(splineRoot);
                UnityEngine.Object.DestroyImmediate(enemy);
            }
        }

        [Test]
        public void CharacterContextIsSharedRootForPlayerEnemyAndNpcSpecializations()
        {
            CharacterContext player = new PlayerLikeCharacterContext
            {
                HasLocalCameraAuthority = true
            };
            CharacterContext enemy = new EnemyLikeCharacterContext
            {
                ThreatScore = 1f
            };
            CharacterContext merchant = new MerchantLikeCharacterContext
            {
                ShopInventoryId = "merchant.basic"
            };

            player.MarkAlive();
            player.Intent.move = Vector2.right;
            player.Intent.attack = true;

            enemy.MarkAlive();
            enemy.Perception.currentTargetId = "player.local";

            merchant.MarkAlive();
            merchant.Intent.interact = true;

            Assert.AreEqual(CoCoLifecycleState.Active, player.Lifecycle.State);
            Assert.AreEqual(CoCoLifecycleState.Active, enemy.Lifecycle.State);
            Assert.AreEqual(CoCoLifecycleState.Active, merchant.Lifecycle.State);
            Assert.IsTrue(player.Intent.attack);
            Assert.AreEqual("player.local", enemy.Perception.currentTargetId);
            Assert.IsTrue(merchant.Intent.interact);
            Assert.IsTrue(((PlayerLikeCharacterContext)player).HasLocalCameraAuthority);
            Assert.AreEqual(1f, ((EnemyLikeCharacterContext)enemy).ThreatScore);
            Assert.AreEqual("merchant.basic", ((MerchantLikeCharacterContext)merchant).ShopInventoryId);
        }

        [Test]
        public void CharacterContextProviderSuppliesDefaultCharacterContext()
        {
            var root = new GameObject("Character Context Provider Test");
            try
            {
                var provider = root.AddComponent<CharacterContextProvider>();
                ICoCoContextProvider<CharacterContext> contextProvider = provider;

                Assert.IsNotNull(provider.Context);
                Assert.AreSame(provider.Context, contextProvider.Context);
                Assert.IsInstanceOf<CharacterContext>(provider.Context);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void DerivedCharacterContextProviderCanExposeBusinessContext()
        {
            var root = new GameObject("Derived Character Context Provider Test");
            try
            {
                var provider = root.AddComponent<TestCharacterProvider>();
                ICoCoContextProvider<CharacterContext> baseProvider = provider;

                provider.Context.DecisionStamp = 7;

                Assert.AreSame(provider.Context, baseProvider.Context);
                Assert.IsInstanceOf<TestCharacterContext>(baseProvider.Context);
                Assert.AreEqual(7, ((TestCharacterContext)baseProvider.Context).DecisionStamp);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CharacterNavigationFactsLiveInsideCharacterContext()
        {
            var root = new GameObject("Character Navigation Context Test");
            try
            {
                var provider = root.AddComponent<CharacterContextProvider>();
                var lifecycle = root.AddComponent<CharacterLifeCycle>();
                var navigationContext = provider.Context.Navigation;

                navigationContext.TryClaimControl("EnemySpline");
                navigationContext.SetDestination(
                    new Vector3(3f, 0f, 2f),
                    2f,
                    0.25f,
                    CharacterNavigationMode.Patrol);

                Assert.AreSame(provider.Context, lifecycle.Context);
                Assert.AreSame(provider.Context.Navigation, navigationContext);
                Assert.IsFalse(typeof(ICoCoContext).IsAssignableFrom(typeof(CharacterNavigationContext)));
                Assert.IsNull(typeof(CharacterContext).Assembly.GetType(
                    "CoCoFlow.Runtime.Gameplay.Character.CharacterNavigation"));
                Assert.AreEqual("EnemySpline", navigationContext.ControlOwner);
                Assert.IsTrue(navigationContext.HasDestination);
                Assert.AreEqual(CharacterNavigationMode.Patrol, navigationContext.Mode);
                Assert.IsNotNull(typeof(CharacterContext).GetProperty("Navigation"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CharacterNavigationMotorUsesFallbackVelocityWithoutNavMeshAgent()
        {
            var root = new GameObject("Character Navigation Motor Fallback Test");

            try
            {
                root.SetActive(false);
                root.AddComponent<CharacterController>();
                var provider = root.AddComponent<CharacterContextProvider>();
                root.AddComponent<CharacterLocomotion>();
                var motor = root.AddComponent<CharacterNavigationMotor>();
                motor.SetContextProvider(provider);
                SetPrivateField(motor, "updateAutomatically", false);
                root.SetActive(true);

                provider.Context.Navigation.SetDestination(
                    new Vector3(0f, 0f, 4f),
                    2f,
                    0.1f,
                    CharacterNavigationMode.Patrol);

                Assert.IsTrue(motor.ExecuteNavigation(0f));
                Assert.IsTrue(provider.Context.Navigation.HasDesiredVelocity);
                Assert.Less(
                    Vector3.Distance(Vector3.forward * 2f, provider.Context.Navigation.DesiredVelocity),
                    0.0001f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CharacterNavigationMotorSyncsAgentNextPositionBeforeReadingVelocity()
        {
            var root = new GameObject("Character Navigation Motor Agent Sync Test");
            NavMeshData navMeshData = null;
            NavMeshDataInstance navMeshInstance = default;

            try
            {
                navMeshData = BuildFlatNavMeshData();
                Assert.IsNotNull(navMeshData);
                navMeshInstance = NavMesh.AddNavMeshData(navMeshData);

                root.SetActive(false);
                root.transform.position = new Vector3(2f, 0f, 0f);
                root.AddComponent<CharacterController>();
                var provider = root.AddComponent<CharacterContextProvider>();
                root.AddComponent<CharacterLocomotion>();
                var agent = root.AddComponent<NavMeshAgent>();
                var motor = root.AddComponent<CharacterNavigationMotor>();
                motor.SetContextProvider(provider);
                SetPrivateField(motor, "updateAutomatically", false);
                root.SetActive(true);

                Assert.IsTrue(agent.Warp(Vector3.zero));
                agent.updatePosition = false;
                root.transform.position = new Vector3(2f, 0f, 0f);
                agent.nextPosition = Vector3.zero;
                float staleDistance = Vector3.Distance(root.transform.position, agent.nextPosition);
                provider.Context.Navigation.SetDestination(
                    new Vector3(2f, 0f, 6f),
                    3f,
                    0.1f,
                    CharacterNavigationMode.Patrol);

                Assert.IsTrue(motor.ExecuteNavigation(0f));
                float syncedDistance = Vector3.Distance(root.transform.position, agent.nextPosition);
                Assert.Less(syncedDistance, staleDistance);
                Assert.Less(syncedDistance, agent.radius);
            }
            finally
            {
                navMeshInstance.Remove();
                UnityEngine.Object.DestroyImmediate(navMeshData);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void EnemyBrainRequiresIntentData()
        {
            LogAssert.Expect(
                LogType.Error,
                new Regex("EnemyBrain.*缺少 EnemyIntentData"));

            var root = new GameObject("Enemy Brain Missing Intent Test");
            try
            {
                var brain = root.AddComponent<EnemyBrain>();

                Assert.IsFalse(brain.enabled);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void EnemyBrainWritesTargetIntentAndNavigationFacts()
        {
            var enemy = new GameObject("Enemy Brain Facts Test");
            var target = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            var intent = ScriptableObject.CreateInstance<EnemyIntentData>();
            var config = ScriptableObject.CreateInstance<EnemyConfigData>();

            try
            {
                enemy.SetActive(false);
                target.name = "Target Player";
                target.layer = 6;
                enemy.transform.position = Vector3.zero;
                target.transform.position = new Vector3(0f, 0f, 4f);

                var provider = enemy.AddComponent<CharacterContextProvider>();
                var lifecycle = enemy.AddComponent<CharacterLifeCycle>();
                var brain = enemy.AddComponent<EnemyBrain>();
                lifecycle.SetContextProvider(provider);
                brain.SetIntentData(intent);
                brain.SetConfigData(config);
                brain.SetCharacterContextProvider(provider);

                enemy.SetActive(true);
                Physics.SyncTransforms();

                Assert.IsTrue(brain.Tick(true));
                Assert.AreSame(target.transform, provider.Context.Perception.currentTarget);
                Assert.AreSame(target.transform, provider.Context.Intent.desiredTarget);
                Assert.IsTrue(provider.Context.Intent.hasMovePosition);
                Assert.IsFalse(provider.Context.Intent.attack);
                Assert.AreEqual(CharacterNavigationMode.Chase, provider.Context.Navigation.Mode);
                Assert.AreEqual("EnemyBrain", provider.Context.Navigation.ControlOwner);
                Assert.IsTrue(provider.Context.Navigation.HasDestination);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
                UnityEngine.Object.DestroyImmediate(intent);
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(enemy);
            }
        }

        [Test]
        public void EnemyVisionQueryUsesColliderCenterForOffsetTargetRoot()
        {
            var observer = new GameObject("Enemy Vision Offset Observer");
            var target = new GameObject("Enemy Vision Offset Target");
            var config = ScriptableObject.CreateInstance<EnemyConfigData>();

            try
            {
                observer.transform.position = new Vector3(0f, 1f, 0f);
                observer.transform.rotation = Quaternion.identity;
                target.layer = 6;
                target.transform.position = new Vector3(0f, 0f, 4f);
                var targetCollider = target.AddComponent<BoxCollider>();
                targetCollider.center = new Vector3(0f, 1f, 0f);
                targetCollider.size = new Vector3(0.5f, 0.5f, 0.5f);

                Physics.SyncTransforms();

                bool found = EnemyVisionQuery.TryFindVisibleTarget(
                    observer.transform,
                    config,
                    1 << 6,
                    null,
                    new Collider[4],
                    out EnemyVisionQueryResult result);

                Assert.IsTrue(found);
                Assert.AreSame(target.transform, result.Target);
                Assert.Less(Vector3.Distance(targetCollider.bounds.center, result.LastKnownPosition), 0.0001f);
                Assert.AreEqual(4f, result.Distance, 0.0001f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(observer);
            }
        }

        [Test]
        public void EnemyVisionQueryUsesResolvedColliderForCurrentTarget()
        {
            var observer = new GameObject("Enemy Vision Current Observer");
            var target = new GameObject("Enemy Vision Current Target");
            var config = ScriptableObject.CreateInstance<EnemyConfigData>();

            try
            {
                observer.transform.position = new Vector3(0f, 1f, 0f);
                observer.transform.rotation = Quaternion.identity;
                target.layer = 6;
                target.transform.position = new Vector3(0f, 0f, 4f);
                var targetCollider = target.AddComponent<BoxCollider>();
                targetCollider.center = new Vector3(0f, 1f, 0f);
                targetCollider.size = new Vector3(0.5f, 0.5f, 0.5f);

                Physics.SyncTransforms();

                bool found = EnemyVisionQuery.TryFindVisibleTarget(
                    observer.transform,
                    config,
                    1 << 6,
                    target.transform,
                    new Collider[0],
                    out EnemyVisionQueryResult result);

                Assert.IsTrue(found);
                Assert.AreSame(target.transform, result.Target);
                Assert.Less(Vector3.Distance(targetCollider.bounds.center, result.LastKnownPosition), 0.0001f);
                Assert.AreEqual(4f, result.Distance, 0.0001f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(observer);
            }
        }

        [Test]
        public void EnemyVisionQueryRejectsCurrentTargetOutsideThreeDimensionalAggroRadius()
        {
            var observer = new GameObject("Enemy Vision Aggro Observer");
            var target = new GameObject("Enemy Vision Elevated Target");
            var config = ScriptableObject.CreateInstance<EnemyConfigData>();

            try
            {
                observer.transform.position = Vector3.zero;
                observer.transform.rotation = Quaternion.identity;
                target.layer = 6;
                target.transform.position = new Vector3(0f, 8f, 9f);
                target.AddComponent<BoxCollider>();
                SetPrivateField(config, "aggroRadius", 10f);

                Physics.SyncTransforms();

                bool found = EnemyVisionQuery.TryFindVisibleTarget(
                    observer.transform,
                    config,
                    1 << 6,
                    target.transform,
                    new Collider[0],
                    out EnemyVisionQueryResult result);

                Assert.IsFalse(found);
                Assert.IsFalse(result.IsVisible);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(observer);
            }
        }

        [Test]
        public void EnemyVisionQueryReturnsRootHorizontalDistanceForAttackRange()
        {
            var observer = new GameObject("Enemy Vision Attack Range Observer");
            var target = new GameObject("Enemy Vision Attack Range Target");
            var config = ScriptableObject.CreateInstance<EnemyConfigData>();

            try
            {
                observer.transform.position = new Vector3(0f, 0f, 0f);
                observer.transform.rotation = Quaternion.identity;
                target.layer = 6;
                target.transform.position = new Vector3(0f, 0f, 2f);
                var targetCollider = target.AddComponent<BoxCollider>();
                targetCollider.center = new Vector3(0f, 1f, 0f);
                targetCollider.size = new Vector3(0.5f, 0.5f, 0.5f);

                Physics.SyncTransforms();

                bool found = EnemyVisionQuery.TryFindVisibleTarget(
                    observer.transform,
                    config,
                    1 << 6,
                    null,
                    new Collider[4],
                    out EnemyVisionQueryResult result);

                Assert.IsTrue(found);
                Assert.AreSame(target.transform, result.Target);
                Assert.Less(Vector3.Distance(targetCollider.bounds.center, result.LastKnownPosition), 0.0001f);
                Assert.AreEqual(2f, result.Distance, 0.0001f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(observer);
            }
        }

        [Test]
        public void EnemyBrainUsesRootHorizontalDistanceForAttackRange()
        {
            var enemy = new GameObject("Enemy Brain Offset Attack Range Test");
            var target = new GameObject("Offset Attack Range Target");
            var intent = ScriptableObject.CreateInstance<EnemyIntentData>();
            var config = ScriptableObject.CreateInstance<EnemyConfigData>();

            try
            {
                enemy.SetActive(false);
                enemy.transform.position = Vector3.zero;
                enemy.transform.rotation = Quaternion.identity;
                target.layer = 6;
                target.transform.position = new Vector3(0f, 0f, 2f);
                var targetCollider = target.AddComponent<BoxCollider>();
                targetCollider.center = new Vector3(0f, 1f, 0f);
                targetCollider.size = new Vector3(0.5f, 0.5f, 0.5f);

                var provider = enemy.AddComponent<CharacterContextProvider>();
                var lifecycle = enemy.AddComponent<CharacterLifeCycle>();
                var brain = enemy.AddComponent<EnemyBrain>();
                lifecycle.SetContextProvider(provider);
                brain.SetIntentData(intent);
                brain.SetConfigData(config);
                brain.SetCharacterContextProvider(provider);

                enemy.SetActive(true);
                Physics.SyncTransforms();

                Assert.IsTrue(brain.Tick(true));
                Assert.AreSame(target.transform, provider.Context.Perception.currentTarget);
                Assert.IsTrue(provider.Context.Intent.attack);
                Assert.IsFalse(provider.Context.Intent.hasMovePosition);
                Assert.AreEqual(CharacterNavigationMode.Combat, provider.Context.Navigation.Mode);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
                UnityEngine.Object.DestroyImmediate(intent);
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(enemy);
            }
        }

        [Test]
        public void EnemyBrainWritesNavigationWithoutTakingControlWhenClaimDisabled()
        {
            var enemy = new GameObject("Enemy Brain No Claim Navigation Test");
            var target = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            var intent = ScriptableObject.CreateInstance<EnemyIntentData>();
            var config = ScriptableObject.CreateInstance<EnemyConfigData>();

            try
            {
                enemy.SetActive(false);
                target.layer = 6;
                enemy.transform.position = Vector3.zero;
                target.transform.position = new Vector3(0f, 0f, 4f);
                SetPrivateField(intent, "claimNavigationOnTargetVisible", false);

                var provider = enemy.AddComponent<CharacterContextProvider>();
                var lifecycle = enemy.AddComponent<CharacterLifeCycle>();
                var brain = enemy.AddComponent<EnemyBrain>();
                lifecycle.SetContextProvider(provider);
                brain.SetIntentData(intent);
                brain.SetConfigData(config);
                brain.SetCharacterContextProvider(provider);

                enemy.SetActive(true);
                Physics.SyncTransforms();

                Assert.IsTrue(brain.Tick(true));
                Assert.AreSame(target.transform, provider.Context.Perception.currentTarget);
                Assert.IsTrue(provider.Context.Intent.hasMovePosition);
                Assert.AreEqual(CharacterNavigationMode.Chase, provider.Context.Navigation.Mode);
                Assert.IsTrue(provider.Context.Navigation.HasDestination);
                Assert.IsFalse(provider.Context.Navigation.HasAnyControl);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
                UnityEngine.Object.DestroyImmediate(intent);
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(enemy);
            }
        }

        [Test]
        public void EnemyBrainReleasesNavigationAfterTargetLost()
        {
            var enemy = new GameObject("Enemy Brain Lost Target Test");
            var target = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            var intent = ScriptableObject.CreateInstance<EnemyIntentData>();
            var config = ScriptableObject.CreateInstance<EnemyConfigData>();

            try
            {
                enemy.SetActive(false);
                target.layer = 6;
                enemy.transform.position = Vector3.zero;
                target.transform.position = new Vector3(0f, 0f, 40f);
                SetPrivateField(intent, "disengageDelay", 0f);

                var provider = enemy.AddComponent<CharacterContextProvider>();
                var lifecycle = enemy.AddComponent<CharacterLifeCycle>();
                var brain = enemy.AddComponent<EnemyBrain>();
                lifecycle.SetContextProvider(provider);
                brain.SetIntentData(intent);
                brain.SetConfigData(config);
                brain.SetCharacterContextProvider(provider);

                provider.Context.Perception.currentTarget = target.transform;
                provider.Context.Perception.currentTargetId = "target";
                provider.Context.Perception.lastKnownPosition = new Vector3(0f, 0f, 4f);
                provider.Context.Navigation.TryClaimControl("EnemyBrain", 10);
                provider.Context.Navigation.SetDestination(
                    provider.Context.Perception.lastKnownPosition,
                    config.ChaseSpeed,
                    0.1f,
                    CharacterNavigationMode.Chase);

                enemy.SetActive(true);
                Physics.SyncTransforms();

                Assert.IsTrue(brain.Tick(true));
                Assert.IsNull(provider.Context.Perception.currentTarget);
                Assert.IsFalse(provider.Context.Intent.hasMovePosition);
                Assert.IsFalse(provider.Context.Navigation.HasAnyControl);
                Assert.IsFalse(provider.Context.Navigation.HasDestination);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
                UnityEngine.Object.DestroyImmediate(intent);
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(enemy);
            }
        }

        [Test]
        public void EnemyBrainClearsEngagementWhenDestroyedTargetComparesNull()
        {
            var enemy = new GameObject("Enemy Brain Destroyed Target Test");
            var target = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            var intent = ScriptableObject.CreateInstance<EnemyIntentData>();
            var config = ScriptableObject.CreateInstance<EnemyConfigData>();

            try
            {
                enemy.SetActive(false);
                target.layer = 6;
                enemy.transform.position = Vector3.zero;
                target.transform.position = new Vector3(0f, 0f, 1f);

                var provider = enemy.AddComponent<CharacterContextProvider>();
                var lifecycle = enemy.AddComponent<CharacterLifeCycle>();
                var brain = enemy.AddComponent<EnemyBrain>();
                lifecycle.SetContextProvider(provider);
                brain.SetIntentData(intent);
                brain.SetConfigData(config);
                brain.SetCharacterContextProvider(provider);

                provider.Context.Perception.currentTarget = target.transform;
                provider.Context.Perception.currentTargetId = "target.destroyed";
                provider.Context.Perception.isTargetVisible = true;
                provider.Context.Intent.desiredTarget = target.transform;
                provider.Context.Intent.desiredTargetId = "target.destroyed";
                provider.Context.Intent.attack = true;
                provider.Context.Navigation.TryClaimControl("EnemyBrain", 10);
                provider.Context.Navigation.SetDestination(
                    target.transform.position,
                    config.ChaseSpeed,
                    intent.AttackRange,
                    CharacterNavigationMode.Chase);

                UnityEngine.Object.DestroyImmediate(target);
                target = null;

                enemy.SetActive(true);
                Physics.SyncTransforms();

                Assert.IsTrue(brain.Tick(true));
                Assert.IsNull(provider.Context.Perception.currentTarget);
                Assert.AreEqual(string.Empty, provider.Context.Perception.currentTargetId);
                Assert.IsFalse(provider.Context.Perception.isTargetVisible);
                Assert.IsNull(provider.Context.Intent.desiredTarget);
                Assert.AreEqual(string.Empty, provider.Context.Intent.desiredTargetId);
                Assert.IsFalse(provider.Context.Intent.attack);
                Assert.IsFalse(provider.Context.Intent.hasMovePosition);
                Assert.IsFalse(provider.Context.Navigation.HasAnyControl);
                Assert.IsFalse(provider.Context.Navigation.HasDestination);
                Assert.IsFalse(provider.Context.Navigation.HasDesiredVelocity);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
                UnityEngine.Object.DestroyImmediate(intent);
                if (target != null)
                {
                    UnityEngine.Object.DestroyImmediate(target);
                }
                UnityEngine.Object.DestroyImmediate(enemy);
            }
        }

        [Test]
        public void CharacterAndItemModelDoesNotIntroducePlayerEnemyOrChestContexts()
        {
            var characterAssembly = typeof(CharacterContext).Assembly;
            var itemAssembly = typeof(ItemContext).Assembly;

            Assert.IsNull(characterAssembly.GetType("CoCoFlow.Runtime.Gameplay.Character.PlayerContext"));
            Assert.IsNull(characterAssembly.GetType("CoCoFlow.Runtime.Gameplay.Character.EnemyContext"));
            Assert.IsNull(itemAssembly.GetType("CoCoFlow.Runtime.Gameplay.Item.ChestContext"));
            Assert.IsNull(itemAssembly.GetType("CoCoFlow.Runtime.Gameplay.Item.ChestState"));

            foreach (var type in itemAssembly.GetTypes())
            {
                Assert.IsFalse(type.Name.Contains("Chest"), $"{type.FullName} should use Item naming.");
            }
        }



        [Test]
        public void ItemContextModelsReusableContainerLifecycleWithoutChestNaming()
        {
            var item = new ItemContext();
            item.Identity.StableEntityId = "item.scene.container.01";
            item.Payload.itemId = "loot.currency";
            item.Payload.count = 5;

            item.SetLocked();
            Assert.AreEqual(ItemSemanticState.Locked, item.ItemState);
            Assert.AreEqual(CoCoLifecycleState.Active, item.Lifecycle.State);

            item.SetAvailable();
            Assert.AreEqual(ItemSemanticState.Available, item.ItemState);
            Assert.AreEqual((int)ItemSemanticState.Available, item.SemanticStateId);

            item.SetOpening();
            Assert.AreEqual(ItemSemanticState.Opening, item.ItemState);
            Assert.AreEqual((int)ItemSemanticState.Opening, item.ActionStateId);

            item.SetOpened();
            Assert.AreEqual(ItemSemanticState.Opened, item.ItemState);
            Assert.AreEqual(0, item.ActionStateId);
            Assert.IsTrue(item.Payload.HasPayload);

            item.SetConsumed();
            Assert.AreEqual(ItemSemanticState.Consumed, item.ItemState);
            Assert.AreEqual(CoCoLifecycleState.Consumed, item.Lifecycle.State);
        }

        [Test]
        public void ItemContextProviderWritesInteractionRequestsIntoItemIntent()
        {
            var root = new GameObject("Item Intent Provider Test");
            try
            {
                var provider = root.AddComponent<ItemContextProvider>();

                Assert.IsInstanceOf<ICoCoContextProvider<ItemContext>>(provider);
                Assert.IsInstanceOf<ICoCoIntentSource<ItemIntent>>(provider);
                Assert.AreSame(provider.Context.Intent, provider.Intent);

                provider.RequestOpen("actor.player");
                Assert.IsTrue(provider.Context.Intent.openRequested);
                Assert.AreEqual("actor.player", provider.Context.Intent.actorId);
                Assert.AreEqual(ItemSemanticState.Inactive, provider.Context.ItemState);

                provider.RequestUnlock("actor.player");
                Assert.IsTrue(provider.Context.Intent.unlockRequested);
                Assert.AreEqual(ItemSemanticState.Inactive, provider.Context.ItemState);

                provider.RequestUse("actor.player");
                Assert.IsTrue(provider.Context.Intent.useRequested);
                Assert.AreEqual(ItemSemanticState.Inactive, provider.Context.ItemState);

                provider.ClearIntent();
                Assert.IsFalse(provider.Context.Intent.openRequested);
                Assert.IsFalse(provider.Context.Intent.unlockRequested);
                Assert.IsFalse(provider.Context.Intent.useRequested);
                Assert.AreEqual(string.Empty, provider.Context.Intent.actorId);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ItemLifeCycleWritesProviderContextAndPublishesItemEvents()
        {
            var root = new GameObject("Item Lifecycle Test");
            var agent = new CoCoEventAgent();
            var openedEvents = 0;
            var consumedEvents = 0;
            var openedEventMatches = false;
            var consumedEventMatches = false;
            var openedEnvelopeObserved = false;
            var consumedEnvelopeObserved = false;

            try
            {
                var provider = root.AddComponent<ItemContextProvider>();
                provider.Context.Identity.StableEntityId = "item.scene.container.01";
                provider.Context.Payload.itemId = "loot.currency";
                provider.Context.Payload.count = 5;

                var lifecycle = root.AddComponent<ItemLifeCycle>();
                lifecycle.SetContextProvider(provider);
                lifecycle.SetActorId("actor.player");

                agent.Subscribe<ItemOpenedEvent>((ref ItemOpenedEvent evt) =>
                {
                    openedEvents++;
                    openedEventMatches =
                        evt.Context == provider.Context &&
                        evt.ItemId == "loot.currency" &&
                        evt.EventSequence == 1;
                });
                agent.Subscribe<ItemConsumedEvent>((ref ItemConsumedEvent evt) =>
                {
                    consumedEvents++;
                    consumedEventMatches =
                        evt.Context == provider.Context &&
                        evt.ItemId == "loot.currency" &&
                        evt.EventSequence == 2;
                });
                agent.Subscribe<CoCoEventEnvelope>((ref CoCoEventEnvelope envelope) =>
                {
                    if (envelope.eventTypeId == "Item.Opened")
                    {
                        openedEnvelopeObserved =
                            envelope.sourceEntityId == "actor.player" &&
                            envelope.targetEntityId == "item.scene.container.01" &&
                            envelope.sequence == 1 &&
                            envelope.payloadTypeId == nameof(ItemOpenedEvent) &&
                            envelope.payload == "loot.currency";
                    }

                    if (envelope.eventTypeId == "Item.Consumed")
                    {
                        consumedEnvelopeObserved =
                            envelope.sourceEntityId == "actor.player" &&
                            envelope.targetEntityId == "item.scene.container.01" &&
                            envelope.sequence == 2 &&
                            envelope.payloadTypeId == nameof(ItemConsumedEvent) &&
                            envelope.payload == "loot.currency";
                    }
                });

                lifecycle.SetAvailable();
                Assert.AreEqual(ItemSemanticState.Available, provider.Context.ItemState);
                Assert.AreSame(provider.Context, lifecycle.Context);

                provider.RequestOpen("actor.player");
                lifecycle.SetOpening();
                lifecycle.SetOpened();
                lifecycle.SetOpened();

                Assert.AreEqual(ItemSemanticState.Opened, provider.Context.ItemState);
                Assert.IsFalse(provider.Context.Intent.openRequested);
                Assert.AreEqual(string.Empty, provider.Context.Intent.actorId);
                Assert.AreEqual(1, openedEvents);
                Assert.IsTrue(openedEventMatches);
                Assert.IsTrue(openedEnvelopeObserved);

                provider.RequestUse("actor.player");
                lifecycle.SetConsumed();

                Assert.AreEqual(ItemSemanticState.Consumed, provider.Context.ItemState);
                Assert.AreEqual(CoCoLifecycleState.Consumed, provider.Context.Lifecycle.State);
                Assert.IsFalse(provider.Context.Intent.useRequested);
                Assert.AreEqual(1, consumedEvents);
                Assert.IsTrue(consumedEventMatches);
                Assert.IsTrue(consumedEnvelopeObserved);
                Assert.AreEqual(2, provider.Context.LastEventSequence);
            }
            finally
            {
                agent.UnsubscribeAll();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ItemLifeCyclePublishesIntentActorBeforeClearingIntent()
        {
            var root = new GameObject("Item Lifecycle Intent Actor Test");
            var agent = new CoCoEventAgent();
            string openedSourceEntityId = string.Empty;
            string consumedSourceEntityId = string.Empty;

            try
            {
                var provider = root.AddComponent<ItemContextProvider>();
                provider.Context.Identity.StableEntityId = "item.scene.intent_actor";
                provider.Context.Payload.itemId = "loot.intent";

                var lifecycle = root.AddComponent<ItemLifeCycle>();
                lifecycle.SetContextProvider(provider);

                agent.Subscribe<CoCoEventEnvelope>((ref CoCoEventEnvelope envelope) =>
                {
                    if (envelope.eventTypeId == "Item.Opened")
                    {
                        openedSourceEntityId = envelope.sourceEntityId;
                    }

                    if (envelope.eventTypeId == "Item.Consumed")
                    {
                        consumedSourceEntityId = envelope.sourceEntityId;
                    }
                });

                provider.RequestOpen("actor.intent");
                lifecycle.SetOpened();
                Assert.AreEqual(string.Empty, provider.Context.Intent.actorId);
                Assert.AreEqual("actor.intent", openedSourceEntityId);

                provider.RequestUse("actor.intent");
                lifecycle.SetConsumed();
                Assert.AreEqual(string.Empty, provider.Context.Intent.actorId);
                Assert.AreEqual("actor.intent", consumedSourceEntityId);
            }
            finally
            {
                agent.UnsubscribeAll();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ItemInputDriverMapsExternalItemIntentIntoItemContext()
        {
            var root = new GameObject("Item Input Driver Test");
            try
            {
                var provider = root.AddComponent<ItemContextProvider>();
                var source = root.AddComponent<TestItemIntentSource>();
                var driver = root.AddComponent<ItemInputDriver>();
                driver.SetContextProvider(provider);
                driver.SetItemIntentSource(source);

                source.Intent.openRequested = true;
                source.Intent.actorId = "actor.player";

                Assert.IsTrue(InvokePrivateBool(driver, "SampleInput"));
                Assert.IsTrue(provider.Context.Intent.openRequested);
                Assert.AreEqual("actor.player", provider.Context.Intent.actorId);
                Assert.AreEqual(ItemSemanticState.Inactive, provider.Context.ItemState);

                driver.ClearIntent();
                Assert.IsFalse(provider.Context.Intent.openRequested);
                Assert.AreEqual(string.Empty, provider.Context.Intent.actorId);

                driver.RequestUnlock("actor.player");
                driver.RequestUse("actor.player");
                Assert.IsTrue(provider.Context.Intent.unlockRequested);
                Assert.IsTrue(provider.Context.Intent.useRequested);
                Assert.AreEqual(ItemSemanticState.Inactive, provider.Context.ItemState);

                Assert.IsNull(typeof(ItemInputDriver).GetMethod(
                    "ChangeState",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }



        [Test]
        public void EventEnvelopeRepresentsItemOpenedAndFireSequences()
        {
            var itemOpened = CoCoEventEnvelope.Create(
                eventTypeId: "Item.Opened",
                sourceEntityId: "actor.player.a",
                sequence: 7,
                tick: 320,
                reliable: true,
                targetEntityId: "item.scene.container.01",
                payloadTypeId: "ItemOpenedEvent",
                payload: "loot.currency");

            Assert.IsTrue(itemOpened.IsValid);
            Assert.IsTrue(itemOpened.HasTarget);
            Assert.IsTrue(itemOpened.HasPayload);
            Assert.AreEqual("actor.player.a", itemOpened.sourceEntityId);
            Assert.AreEqual("item.scene.container.01", itemOpened.targetEntityId);
            Assert.AreEqual(320, itemOpened.tick);
            Assert.IsTrue(itemOpened.reliable);

            var fire = CoCoEventEnvelope.Create(
                eventTypeId: "Character.Fire",
                sourceEntityId: "actor.player.a",
                sequence: 128,
                tick: 512,
                reliable: false);

            Assert.IsTrue(fire.IsValid);
            Assert.AreEqual(128, fire.sequence);
            Assert.AreEqual("Character.Fire", fire.eventTypeId);
            Assert.IsFalse(fire.HasTarget);
            Assert.IsFalse(fire.HasPayload);
        }

        [Test]
        public void EventBusPublishesTypedEventAndEnvelopeForNetworkBridge()
        {
            var agent = new CoCoEventAgent();
            var typedObserved = false;
            var envelopeObserved = false;

            try
            {
                agent.Subscribe<ItemOpenedEvent>((ref ItemOpenedEvent evt) =>
                {
                    typedObserved = evt.ItemId == "loot.currency";
                });
                agent.Subscribe<CoCoEventEnvelope>((ref CoCoEventEnvelope envelope) =>
                {
                    envelopeObserved = envelope.eventTypeId == "Item.Opened" &&
                                       envelope.sourceEntityId == "actor.player.a" &&
                                       envelope.targetEntityId == "item.scene.container.01" &&
                                       envelope.sequence == 7;
                });

                var itemContext = new ItemContext();
                itemContext.Identity.StableEntityId = "item.scene.container.01";
                itemContext.SetOpened();

                var itemOpenedEvent = new ItemOpenedEvent
                {
                    Context = itemContext,
                    ItemId = "loot.currency",
                    EventSequence = itemContext.NextEventSequence()
                };
                var envelope = CoCoEventEnvelope.Create(
                    eventTypeId: "Item.Opened",
                    sourceEntityId: "actor.player.a",
                    sequence: 7,
                    tick: 320,
                    reliable: true,
                    targetEntityId: itemContext.Identity.StableEntityId,
                    payloadTypeId: nameof(ItemOpenedEvent),
                    payload: itemOpenedEvent.ItemId);

                CoCoEventBus.PublishWithEnvelope(ref itemOpenedEvent, ref envelope);

                Assert.IsTrue(typedObserved);
                Assert.IsTrue(envelopeObserved);
                Assert.AreEqual(ItemSemanticState.Opened, itemContext.ItemState);
            }
            finally
            {
                agent.UnsubscribeAll();
            }
        }

        [Test]
        public void PersistenceUniqueIdMapsToStableEntityIdWithoutOwningRuntimeId()
        {
            var root = new GameObject("Persistence Identity Test");
            try
            {
                var persistenceContext = root.AddComponent<PersistenceContext>();
                SetPrivateField(persistenceContext, "stableEntityId", "scene.item.001");

                Assert.IsInstanceOf<ICoCoStableEntityIdProvider>(persistenceContext);
                Assert.AreEqual("scene.item.001", persistenceContext.StableEntityId);

                var context = new ItemContext();
                context.Identity.StableEntityId = persistenceContext.StableEntityId;
                context.Identity.RuntimeInstanceId = "runtime.host.42";

                Assert.IsTrue(context.Identity.HasStableEntityId);
                Assert.IsTrue(context.Identity.HasRuntimeInstanceId);
                Assert.AreEqual("scene.item.001", context.Identity.StableEntityId);
                Assert.AreEqual("runtime.host.42", context.Identity.RuntimeInstanceId);
                Assert.AreNotEqual(context.Identity.StableEntityId, context.Identity.RuntimeInstanceId);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void EventSequenceIsSeparateFromStableAndRuntimeEntityIds()
        {
            var context = new CharacterContext();
            context.Identity.StableEntityId = "scene.actor.template";
            context.Identity.RuntimeInstanceId = "runtime.actor.001";

            var first = context.NextEventSequence();
            var second = context.NextEventSequence();
            var envelope = CoCoEventEnvelope.Create(
                eventTypeId: "Character.Fire",
                sourceEntityId: context.Identity.RuntimeInstanceId,
                sequence: second,
                tick: 900,
                reliable: false);

            Assert.AreEqual(1, first);
            Assert.AreEqual(2, second);
            Assert.AreEqual(2, envelope.sequence);
            Assert.AreEqual("runtime.actor.001", envelope.sourceEntityId);
            Assert.AreNotEqual(context.Identity.StableEntityId, envelope.sourceEntityId);
        }



        [Test]
        public void CharacterInputDriverMapsExplicitActionsIntoCharacterIntent()
        {
            var root = new GameObject("Character Input Driver Test");
            CharacterInputFixture input = null;
            try
            {
                var provider = root.AddComponent<CharacterContextProvider>();
                var driver = root.AddComponent<CharacterInputDriver>();
                input = CreateCharacterInputFixture();
                driver.SetContextProvider(provider);
                input.Configure(driver);

                input.SetMove(new Vector2(0.75f, -0.25f));
                input.SetLook(new Vector2(0.5f, 0.25f));
                input.Press(input.Gamepad.buttonWest);

                Assert.IsTrue(InvokePrivateBool(driver, "SampleInput"));
                Assert.AreEqual(
                    new Vector2(0.75f, -0.25f),
                    provider.Context.Intent.move);
                Assert.AreEqual(
                    new Vector2(0.5f, 0.25f),
                    provider.Context.Intent.look);
                Assert.IsTrue(provider.Context.Intent.attack);
                Assert.IsFalse(provider.Context.Intent.interact);

                Assert.IsTrue(InvokePrivateBool(driver, "SampleInput"));
                Assert.IsFalse(provider.Context.Intent.attack);
                Assert.IsFalse(provider.Context.Intent.interact);

                input.Press(input.Gamepad.buttonNorth);
                Assert.IsTrue(InvokePrivateBool(driver, "SampleInput"));
                Assert.IsFalse(provider.Context.Intent.attack);
                Assert.IsTrue(provider.Context.Intent.interact);

                input.Press(input.Gamepad.buttonEast);
                Assert.IsTrue(InvokePrivateBool(driver, "SampleInput"));
                Assert.IsFalse(provider.Context.Intent.interact);
                Assert.IsTrue(provider.Context.Intent.useSkill);

                Assert.IsNull(typeof(CharacterInputDriver).GetMethod(
                    "ChangeState",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic));
            }
            finally
            {
                input?.Destroy();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CharacterInputDriverRequiresAnExplicitInputReader()
        {
            var root = new GameObject("Character Input Driver Service Test");
            try
            {
                var provider = root.AddComponent<CharacterContextProvider>();
                var driver = root.AddComponent<CharacterInputDriver>();
                driver.SetContextProvider(provider);

                Assert.IsFalse(InvokePrivateBool(driver, "SampleInput"));
                Assert.AreEqual(Vector2.zero, provider.Context.Intent.move);
                Assert.IsFalse(provider.Context.Intent.jump);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CharacterLifeCycleWritesProviderContextAndKeepsEvents()
        {
            var root = new GameObject("Character LifeCycle Test");
            var deathEventReceived = false;

            try
            {
                var provider = root.AddComponent<CharacterContextProvider>();
                var lifecycle = root.AddComponent<CharacterLifeCycle>();
                lifecycle.SetContextProvider(provider);
                lifecycle.OnDeath += () => deathEventReceived = true;

                lifecycle.TakeDamage(provider.Context.Resources.MaxHealth);

                Assert.IsFalse(typeof(ICoCoContextProvider<CharacterContext>).IsAssignableFrom(typeof(CharacterLifeCycle)));
                Assert.AreSame(provider.Context, lifecycle.Context);
                Assert.IsTrue(deathEventReceived);
                Assert.IsTrue(lifecycle.IsDead);
                Assert.AreEqual(0f, lifecycle.CurrentHealth);
                Assert.AreEqual((int)CharacterSemanticState.Dead, provider.Context.SemanticStateId);
                Assert.AreEqual(CoCoLifecycleState.Disabled, provider.Context.Lifecycle.State);

                lifecycle.Revive(0.5f);

                Assert.IsFalse(lifecycle.IsDead);
                Assert.AreEqual(CoCoLifecycleState.Active, provider.Context.Lifecycle.State);
                Assert.AreEqual(provider.Context.Resources.MaxHealth * 0.5f, lifecycle.CurrentHealth);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ItemPersistenceFactsComeFromEntityContext()
        {
            var context = new ItemContext();
            context.Identity.StableEntityId = "item.persist";
            context.SetOpened();

            CoCoEntityContext entityContext = context;

            Assert.AreEqual("item.persist", entityContext.Identity.StableEntityId);
            Assert.AreEqual(CoCoLifecycleState.Active, entityContext.Lifecycle.State);
            Assert.AreEqual((int)ItemSemanticState.Opened, entityContext.SemanticStateId);
        }



        #endregion

        #region Internal Logic

        private static CharacterInputFixture CreateCharacterInputFixture()
        {
            var inputFixture = new InputTestFixture();
            inputFixture.Setup();
            Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
            var actions = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = new InputActionMap("Gameplay");
            InputAction move = map.AddAction(
                "Move",
                InputActionType.Value,
                "<Gamepad>/leftStick");
            InputAction look = map.AddAction(
                "Look",
                InputActionType.Value,
                "<Gamepad>/rightStick");
            InputAction jump = map.AddAction(
                "Jump",
                InputActionType.Button,
                "<Gamepad>/buttonSouth");
            InputAction attack = map.AddAction(
                "Attack",
                InputActionType.Button,
                "<Gamepad>/buttonWest");
            InputAction interact = map.AddAction(
                "Interact",
                InputActionType.Button,
                "<Gamepad>/buttonNorth");
            InputAction useSkill = map.AddAction(
                "UseSkill",
                InputActionType.Button,
                "<Gamepad>/buttonEast");
            move.ChangeBinding(0).WithGroup("Gamepad");
            look.ChangeBinding(0).WithGroup("Gamepad");
            jump.ChangeBinding(0).WithGroup("Gamepad");
            attack.ChangeBinding(0).WithGroup("Gamepad");
            interact.ChangeBinding(0).WithGroup("Gamepad");
            useSkill.ChangeBinding(0).WithGroup("Gamepad");
            actions.AddActionMap(map);
            actions.AddControlScheme("Gamepad")
                .WithRequiredDevice("<Gamepad>");

            var inputObject = new GameObject("Character Input Reader");
            inputObject.SetActive(false);
            PlayerInput playerInput = inputObject.AddComponent<PlayerInput>();
            playerInput.actions = actions;
            playerInput.defaultActionMap = map.name;
            playerInput.defaultControlScheme = "Gamepad";
            InputReader reader = inputObject.AddComponent<InputReader>();
            inputObject.SetActive(true);
            playerInput.ActivateInput();
            reader.enabled = false;
            reader.enabled = true;

            return new CharacterInputFixture(
                inputObject,
                actions,
                inputFixture,
                reader,
                gamepad,
                InputActionReference.Create(move),
                InputActionReference.Create(look),
                InputActionReference.Create(jump),
                InputActionReference.Create(attack),
                InputActionReference.Create(interact),
                InputActionReference.Create(useSkill));
        }


        private sealed class TestCharacterContext : CharacterContext
        {
            public int DecisionStamp;
        }

        private sealed class PlayerLikeCharacterContext : CharacterContext
        {
            public bool HasLocalCameraAuthority;
        }

        private sealed class EnemyLikeCharacterContext : CharacterContext
        {
            public float ThreatScore;
        }

        private sealed class MerchantLikeCharacterContext : CharacterContext
        {
            public string ShopInventoryId;
        }

        private sealed class CharacterInputFixture
        {
            private readonly GameObject _gameObject;
            private readonly InputActionAsset _actions;
            private readonly InputTestFixture _inputFixture;
            private readonly InputActionReference[] _references;

            public CharacterInputFixture(
                GameObject gameObject,
                InputActionAsset actions,
                InputTestFixture inputFixture,
                InputReader reader,
                Gamepad gamepad,
                InputActionReference move,
                InputActionReference look,
                InputActionReference jump,
                InputActionReference attack,
                InputActionReference interact,
                InputActionReference useSkill)
            {
                _gameObject = gameObject;
                _actions = actions;
                _inputFixture = inputFixture;
                Reader = reader;
                Gamepad = gamepad;
                _references = new[]
                {
                    move,
                    look,
                    jump,
                    attack,
                    interact,
                    useSkill
                };
            }

            public InputReader Reader { get; }
            public Gamepad Gamepad { get; }

            public void Configure(CharacterInputDriver driver)
            {
                SetPrivateField(driver, "moveAction", _references[0]);
                SetPrivateField(driver, "lookAction", _references[1]);
                SetPrivateField(driver, "jumpAction", _references[2]);
                SetPrivateField(driver, "attackAction", _references[3]);
                SetPrivateField(driver, "interactAction", _references[4]);
                SetPrivateField(driver, "useSkillAction", _references[5]);
                driver.SetInputReader(Reader);
            }

            public void SetMove(Vector2 value)
            {
                _inputFixture.Set(Gamepad.leftStick, value);
            }

            public void SetLook(Vector2 value)
            {
                _inputFixture.Set(Gamepad.rightStick, value);
            }

            public void Press(ButtonControl button)
            {
                _inputFixture.Press(button);
                _inputFixture.Release(button);
            }

            public void Destroy()
            {
                UnityEngine.Object.DestroyImmediate(_gameObject);
                foreach (InputActionReference reference in _references)
                {
                    UnityEngine.Object.DestroyImmediate(reference);
                }

                UnityEngine.Object.DestroyImmediate(_actions);
                _inputFixture.TearDown();
            }
        }

        private sealed class TestItemIntentSource :
            MonoBehaviour,
            ICoCoIntentSource<ItemIntent>
        {
            public ItemIntent Intent { get; } = new ItemIntent();
        }

        private static NavMeshData BuildFlatNavMeshData()
        {
            var source = new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box,
                size = new Vector3(20f, 0.1f, 20f),
                transform = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one),
                area = 0
            };

            return NavMeshBuilder.BuildNavMeshData(
                NavMesh.GetSettingsByID(0),
                new List<NavMeshBuildSource> { source },
                new Bounds(Vector3.zero, new Vector3(20f, 5f, 20f)),
                Vector3.zero,
                Quaternion.identity);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = FindPrivateField(target.GetType(), fieldName);
            Assert.IsNotNull(field);
            field.SetValue(target, value);
        }

        private static FieldInfo FindPrivateField(Type type, string fieldName)
        {
            while (type != null)
            {
                var field = type.GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null) return field;

                type = type.BaseType;
            }

            return null;
        }

        private static bool InvokePrivateBool(object target, string methodName)
        {
            var method = target.GetType().GetMethod(
                methodName,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            return (bool)method.Invoke(target, null);
        }

        private sealed class TestCharacterProvider : CharacterContextProvider<TestCharacterContext>
        {
            private readonly TestCharacterContext _context = new TestCharacterContext();

            public override TestCharacterContext Context => _context;
        }

        private sealed class TestCharacterContextSource :
            MonoBehaviour,
            ICharacterContextSource
        {
            private int _priority;
            private Vector2 _move;
            private bool _attack;

            public int Priority => _priority;

            public void Configure(int priority, Vector2 move, bool attack)
            {
                _priority = priority;
                _move = move;
                _attack = attack;
            }

            public void WriteToContext(CharacterContext context)
            {
                context.Intent.move = _move;
                context.Intent.attack = _attack;
            }
        }

        #endregion
    }
}
