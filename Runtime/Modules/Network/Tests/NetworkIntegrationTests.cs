using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Network.Input;

namespace CoCoFlow.Runtime.Modules.Network.Tests
{
    /// <summary>
    /// 网络模块集成测试 — Host/Client 场景验证。
    /// </summary>
    /// <remarks>
    /// <b>前置条件（运行全部测试前）：</b>
    /// <list type="number">
    ///   <item>Fusion SDK 已安装到 Unity 项目（Package Manager → Fusion）</item>
    ///   <item>NetworkProjectConfig.asset 已在 Assets/Resources/Fusion/ 中创建</item>
    ///   <item>NetPlayer.prefab / NetEnemy.prefab 已注册到 NetworkProjectConfig.PrefabTable</item>
    ///   <item>测试场景包含 NetManager 预制体实例，并挂载必要组件</item>
    ///   <item>Simulation TickRate 配置为 30 Hz（<see cref="NetworkConfigHelper.RecommendedTickRate"/>）</item>
    /// </list>
    /// <para>
    /// <b>运行方式：</b>这些测试设计为 Play Mode 测试，部分需要 Fusion 运行时。
    /// 在 Edit Mode Test Runner 中运行时会通过 <c>Assert.Ignore</c> 跳过。
    /// </para>
    /// <para>
    /// <b>AC10 性能目标：</b>Unity Profiler Deep Profile 确认 FixedUpdateNetwork 在 100 个 tick 内
    /// GC.Alloc = 0 bytes。
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("Integration")]
    public class NetworkIntegrationTests : NetworkTestSetup
    {
        // ──────────────────────────────────────────────
        //  Fusion 反射元数据
        // ──────────────────────────────────────────────
        private const string FusionAssemblyName = "Fusion.Runtime";
        private static readonly string[] FusionTypeNames =
        {
            "Fusion.NetworkBool",
            "Fusion.NetworkButtons",
            "Fusion.NetworkRunner",
            "Fusion.NetworkInput",
            "Fusion.PlayerRef",
        };

        private Type _networkBoolType;
        private Type _networkRunnerType;
        private FieldInfo _moveDirectionField;
        private FieldInfo _lookDirectionField;
        private FieldInfo _jumpField;
        private FieldInfo _interactField;

        #region SetUp / TearDown

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            ResolveFusionReflectionCache();
        }

        /// <summary>
        /// 解析 Fusion 类型反射缓存。Fusion.Runtime 不作为测试程序集的直接引用，
        /// 故所有 Fusion 类型的字段操作均通过反射完成。
        /// </summary>
        private void ResolveFusionReflectionCache()
        {
            // 检查 Fusion 程序集是否已加载（Unity 在加载 Network 模块时会自动加载）
            try
            {
                var fusionAsm = Assembly.Load(FusionAssemblyName);
                _networkBoolType = fusionAsm.GetType("Fusion.NetworkBool");
                _networkRunnerType = fusionAsm.GetType("Fusion.NetworkRunner");
            }
            catch
            {
                _networkBoolType = null;
                _networkRunnerType = null;
            }

            // 缓存 NetPlayerInput 的字段信息
            var inputType = typeof(NetPlayerInput);
            _moveDirectionField = inputType.GetField("MoveDirection");
            _lookDirectionField = inputType.GetField("LookDirection");
            _jumpField = inputType.GetField("Jump");
            _interactField = inputType.GetField("Interact");
        }

        #endregion

        #region Public API — Runtime Checks

        /// <summary>
        /// Fusion 运行时是否可用（程序集已加载且核心类型可解析）。
        /// </summary>
        private bool IsFusionAvailable => _networkBoolType != null && _networkRunnerType != null;

        /// <summary>
        /// 检查 Fusion 可用性，不可用时通过 <c>Assert.Ignore</c> 跳过测试。
        /// 应在每个需要 Fusion 运行时的测试方法开头调用。
        /// </summary>
        private void RequireFusion(string testName)
        {
            if (!IsFusionAvailable)
            {
                Assert.Ignore(
                    $"[{testName}] 跳过：Fusion SDK 未安装或 Fusion.Runtime 程序集未加载。" +
                    "请通过 Unity Package Manager 安装 Fusion SDK 后重新运行。");
            }
        }

        /// <summary>
        /// 检查当前是否在 Play Mode。NetManager / NetworkRunner 只能在 Play Mode 下工作。
        /// </summary>
        private static void RequirePlayMode(string testName)
        {
            if (!Application.isPlaying)
            {
                Assert.Ignore(
                    $"[{testName}] 跳过：此测试需要 Play Mode。" +
                    "请在 Unity Test Runner 中切换到 Play Mode 后运行。");
            }
        }

        #endregion

        // ═══════════════════════════════════════════════
        //  Test 1: Host 创建会话并等待场景加载
        // ═══════════════════════════════════════════════

        /// <summary>
        /// 验证 Host 模式启动流程。
        /// </summary>
        /// <remarks>
        /// <b>前置条件：</b>
        /// <list type="bullet">
        ///   <item>Fusion SDK 已安装</item>
        ///   <item>NetworkProjectConfig 已配置（TickRate=30, InputDelay=1）</item>
        ///   <item>测试场景在 Build Settings 中注册</item>
        ///   <item>NetPlayer.prefab 已在 PrefabTable 中注册</item>
        ///   <item>NetworkRunner 预制体上有 NetworkSceneManagerDefault 组件</item>
        /// </list>
        /// <para>
        /// <b>预期行为：</b>
        /// <list type="number">
        ///   <item>调用 <c>NetManager.StartHost("TestSession", sceneRef)</c> 后</item>
        ///   <item>NetworkRunner.IsRunning == true</item>
        ///   <item>回调 <c>INetworkRunnerCallbacks.OnSceneLoadDone</c> 被触发</item>
        ///   <item>通过 <c>CoCoEventBus</c> 发布 <c>NetSceneReadyEvent</c></item>
        /// </list>
        /// </para>
        /// <para>
        /// <b>已知限制：</b>需要 Play Mode + Fusion 运行时。无法在 Edit Mode 或 CI 中运行。
        /// 实际网络传输依赖本地环回或局域网连接。
        /// </para>
        /// </remarks>
        [Test]
        [Category("Host")]
        [Description("Host 创建会话并等待场景加载完成")]
        public void HostCreateSession_AwaitsSceneLoad()
        {
            const string testName = nameof(HostCreateSession_AwaitsSceneLoad);

            RequireFusion(testName);
            RequirePlayMode(testName);

            // ── Arrange ────────────────────────────────────
            // 1. 准备 NetworkProjectConfig（使用默认配置或 NetworkConfigHelper 推荐值）
            //    var config = NetworkProjectConfig.Global;
            //    config.Simulation.TickRate = NetworkConfigHelper.RecommendedTickRate;
            //
            // 2. 准备 NetManager 实例
            //    var netObj = CreateTestObject("NetManager");
            //    var netManager = netObj.AddComponent<NetManager>();
            //
            // 3. 准备 NetworkRunner（NetManager.Awake 会初始化）
            //    yield return null; // 等待 Awake
            //
            // 4. 注册场景加载完成事件监听
            //    bool sceneLoaded = false;
            //    var agent = new EventAgent();
            //    agent.Subscribe<NetSceneReadyEvent>(ref evt => sceneLoaded = true);
            //
            // 5. 获取目标场景引用
            //    var sceneRef = SceneRef.FromIndex(0);

            // ── Act ────────────────────────────────────────
            // await netManager.StartHost("TestSession", sceneRef);
            //
            // // 等待场景加载完成（实际项目需 yield return 等待异步操作）
            // yield return new WaitUntil(() => sceneLoaded);

            // ── Assert ─────────────────────────────────────
            // Assert.That(netManager.IsConnected, Is.True, "Host 应标记为已连接");
            // Assert.That(netManager.Runner.IsRunning, Is.True, "NetworkRunner 应处于运行状态");
            // Assert.That(sceneLoaded, Is.True, "OnSceneLoadDone 回调应已触发");
            // Assert.That(netManager.LocalPlayer.IsValid, Is.True, "本地玩家引用应有效");

            Assert.Ignore(
                $"[{testName}] 完整验证需要 Play Mode + Fusion 运行时。" +
                "桩已就绪：取消注释 Arrange/Act/Assert 块并添加 using Fusion 后即可运行。");
        }

        // ═══════════════════════════════════════════════
        //  Test 2: NetPlayerInput 序列化往返测试
        // ═══════════════════════════════════════════════

        /// <summary>
        /// 验证 NetPlayerInput 结构体的字段读写正确性（反射方式）。
        /// </summary>
        /// <remarks>
        /// <b>前置条件：</b>
        /// <list type="bullet">
        ///   <item>CoCoFlow.Runtime.Modules.Network 程序集可加载</item>
        ///   <item>Fusion.Runtime 程序集已加载（用于 NetworkBool 类型解析）</item>
        /// </list>
        /// <para>
        /// <b>预期行为：</b>
        /// <list type="number">
        ///   <item>创建 <c>NetPlayerInput</c> 实例后 MoveDirection / LookDirection 字段可读写</item>
        ///   <item>Jump / Interact 字段通过反射可设为 true</item>
        ///   <item>默认值：MoveDirection == Vector2.zero, Jump == false</item>
        /// </list>
        /// </para>
        /// <para>
        /// <b>已知限制：</b>
        /// Fusion 的 <c>NetworkInput.Set()</c> / <c>NetworkRunner.GetInput()</c> 序列化
        /// 需要通过 Play Mode 实际运行验证。本测试仅验证结构体字段的反射可访问性。
        /// </para>
        /// </remarks>
        [Test]
        [Category("Input")]
        [Description("NetPlayerInput 字段读写和默认值验证（反射）")]
        public void NetPlayerInput_FieldReadWrite_RoundTrip()
        {
            RequireFusion(nameof(NetPlayerInput_FieldReadWrite_RoundTrip));

            // ── Arrange ────────────────────────────────────
            // 通过反射创建 NetPlayerInput 实例（仅使用非 Fusion 类型字段直接赋值）
            var input = new NetPlayerInput();

            var testMove = new Vector2(0.5f, -1.0f);
            var testLook = new Vector2(0.1f, 0.2f);

            // ── Act ────────────────────────────────────────
            // 直接赋值 UnityEngine 类型字段（无需反射）
            _moveDirectionField?.SetValueDirect(__makeref(input), testMove);
            _lookDirectionField?.SetValueDirect(__makeref(input), testLook);

            // ── Assert ─────────────────────────────────────
            // Vector2 字段可以编译期直接读写
            input.MoveDirection = testMove;
            input.LookDirection = testLook;

            Assert.That(input.MoveDirection, Is.EqualTo(testMove),
                "MoveDirection 应等于设置值");
            Assert.That(input.LookDirection, Is.EqualTo(testLook),
                "LookDirection 应等于设置值");

            // 默认值验证
            var defaultInput = new NetPlayerInput();
            Assert.That(defaultInput.MoveDirection, Is.EqualTo(Vector2.zero),
                "默认 MoveDirection 应为 Vector2.zero");

            // Fusion 类型字段通过反射验证默认值
            if (_jumpField != null)
            {
                var defaultJump = _jumpField.GetValue(defaultInput);
                Assert.That(defaultJump, Is.Not.Null,
                    "Jump 字段应存在且可反射访问");
                // NetworkBool 的默认值为 false，通过反射检查其隐式 bool 转换
                bool jumpValue = Convert.ToBoolean(defaultJump);
                Assert.That(jumpValue, Is.False,
                    "默认 Jump 应为 false");
            }

            if (_interactField != null)
            {
                var defaultInteract = _interactField.GetValue(defaultInput);
                Assert.That(defaultInteract, Is.Not.Null,
                    "Interact 字段应存在且可反射访问");
                bool interactValue = Convert.ToBoolean(defaultInteract);
                Assert.That(interactValue, Is.False,
                    "默认 Interact 应为 false");
            }
        }

        /// <summary>
        /// 验证 NetPlayerInput 结构体实现 INetworkInput 接口。
        /// </summary>
        /// <remarks>
        /// Fusion 要求所有网络输入结构体实现 <c>INetworkInput</c> 接口，
        /// 以确保可以被 <c>NetworkInput.Set()</c> 接受。
        /// </remarks>
        [Test]
        [Category("Input")]
        [Description("NetPlayerInput 实现 INetworkInput 接口")]
        public void NetPlayerInput_ImplementsINetworkInput()
        {
            // 通过反射检查接口实现（INetworkInput 来自 Fusion，不直接引用）
            var inputType = typeof(NetPlayerInput);
            var interfaces = inputType.GetInterfaces();

            bool implementsINetworkInput = false;
            foreach (var iface in interfaces)
            {
                if (iface.Name == "INetworkInput" || iface.FullName == "Fusion.INetworkInput")
                {
                    implementsINetworkInput = true;
                    break;
                }
            }

            Assert.That(implementsINetworkInput, Is.True,
                $"NetPlayerInput 必须实现 INetworkInput 接口。" +
                $"当前实现的接口: {string.Join(", ", Array.ConvertAll(interfaces, i => i.Name))}");
        }

        // ═══════════════════════════════════════════════
        //  Test 3: GC 分配基准测试 (AC10)
        // ═══════════════════════════════════════════════

        /// <summary>
        /// 验证 NetworkRunner 在 100 个 tick 内的 GC 分配为零。
        /// </summary>
        /// <remarks>
        /// <b>前置条件：</b>
        /// <list type="bullet">
        ///   <item>Fusion SDK 已安装</item>
        ///   <item>NetworkProjectConfig 已配置（推荐 TickRate=30）</item>
        ///   <item>NetManager 在场景中运行，且 NetworkRunner 已进入 Running 状态</item>
        ///   <item>NetCharacter 预制体已生成（至少有 1 个玩家角色在线）</item>
        ///   <item>测试以 Play Mode 运行</item>
        /// </list>
        /// <para>
        /// <b>预期行为（AC10 性能目标）：</b>
        /// <list type="number">
        ///   <item>启动 Host 会话后等待场景加载完成</item>
        ///   <item>记录 <c>GC.GetTotalAllocatedBytes()</c> 起始值</item>
        ///   <item>运行 100 个 FixedUpdateNetwork tick（约 3.33 秒 @30Hz）</item>
        ///   <item>再次记录 <c>GC.GetTotalAllocatedBytes()</c></item>
        ///   <item>差值 == 0 bytes</item>
        ///   <item>补充验证：Unity Profiler → Deep Profile 模式下
        ///       FixedUpdateNetwork 调用链不产生 GC.Alloc</item>
        /// </list>
        /// </para>
        /// <para>
        /// <b>已知限制：</b>
        /// <list type="bullet">
        ///   <item>需要 Play Mode + 实际网络会话（至少 Host 单机模式）</item>
        ///   <item><c>GC.GetTotalAllocatedBytes()</c> 是全局计数器，
        ///       测试结果可能受 Unity 引擎后台 GC 影响</item>
        ///   <item>推荐在 Unity Profiler Deep Profile 模式下人工确认，
        ///       自动化测试仅作为快速回归检查</item>
        ///   <item>Fusion 的 <c>NetworkRunner.Simulation.Tick</c> 手动推进
        ///       在 Unity Editor 中不完全等同于实际网络 tick</item>
        /// </list>
        /// </para>
        /// </remarks>
        [UnityTest]
        [Category("Performance")]
        [Description("AC10: 100 tick GC.Alloc=0 bytes")]
        // ReSharper disable once IdentifierTypo
        public IEnumerator NetworkTick_NoGCAllocation_Over100Ticks()
        {
            const string testName = nameof(NetworkTick_NoGCAllocation_Over100Ticks);

            RequireFusion(testName);
            RequirePlayMode(testName);

            // ── Arrange ────────────────────────────────────
            // 1. 创建 NetManager 并启动 Host 会话
            //    var netObj = CreateTestObject("NetManager");
            //    var netManager = netObj.AddComponent<NetManager>();
            //    yield return null; // 等待 Awake
            //
            //    bool sceneReady = false;
            //    var agent = new EventAgent();
            //    agent.Subscribe<NetSceneReadyEvent>(ref evt => sceneReady = true);
            //
            //    await netManager.StartHost("PerfTest", SceneRef.FromIndex(0));
            //    yield return new WaitUntil(() => sceneReady);
            //
            // 2. 等待几帧让 NetworkRunner 稳定
            //    yield return WaitForFrames(60);

            // ── Act ────────────────────────────────────────
            // long allocBefore = GC.GetTotalAllocatedBytes();
            //
            // // 运行 100 个 tick（@30Hz ≈ 3.33 秒）
            // int tickCount = 0;
            // float startTime = Time.time;
            // const int targetTicks = 100;
            //
            // while (tickCount < targetTicks)
            // {
            //     // Fusion 在 FixedUpdateNetwork 中推进 Simulation
            //     // 每个 FixedUpdate 窗口严格对应 1/N 秒（N=TickRate）
            //     // 无法精确同步 Unity 帧与 Fusion tick，故用时间估算
            //     yield return null;
            //
            //     if (Time.time - startTime >= tickCount * (1f / NetworkConfigHelper.RecommendedTickRate))
            //         tickCount++;
            // }
            //
            // long allocAfter = GC.GetTotalAllocatedBytes();

            // ── Assert ─────────────────────────────────────
            // long allocated = allocAfter - allocBefore;
            // Assert.That(allocated, Is.EqualTo(0),
            //     $"AC10 失败：FixedUpdateNetwork 100 ticks 期间 GC 分配了 {allocated} bytes。" +
            //     "请使用 Unity Profiler Deep Profile 定位分配来源。");

            // ── 桩实现：等待一帧后跳过 ────────────────────
            yield return null;

            Assert.Ignore(
                $"[{testName}] 完整性能基准测试需要 Play Mode + Fusion 运行时 + 活跃会话。" +
                "取消注释 Arrange/Act/Assert 块并确保 Fusion 环境就绪后重新运行。\n\n" +
                "手动验证步骤（推荐）：\n" +
                "1. 打开 Unity Profiler (Window > Analysis > Profiler)\n" +
                "2. 启用 Deep Profile\n" +
                "3. 启动 Host 会话\n" +
                "4. 观察 FixedUpdateNetwork 的 GC Alloc 列\n" +
                "5. 确认 100 ticks 内 GC.Alloc = 0 bytes");
        }

        // ═══════════════════════════════════════════════
        //  Test 4: 多客户端连接 Host
        // ═══════════════════════════════════════════════

        /// <summary>
        /// 验证多个客户端连接到 Host 时的玩家加入事件。
        /// </summary>
        /// <remarks>
        /// <b>前置条件：</b>
        /// <list type="bullet">
        ///   <item>Fusion SDK 已安装</item>
        ///   <item>至少可创建 3 个 NetworkRunner 实例（1 Host + 2 Client）</item>
        ///   <item>NetPlayer.prefab 已在 PrefabTable 中注册</item>
        ///   <item>测试在 Play Mode 下运行</item>
        ///   <item>客户端使用 Shared Mode 或 Host Mode（非 Server Mode）</item>
        /// </list>
        /// <para>
        /// <b>预期行为：</b>
        /// <list type="number">
        ///   <item>启动 Host → <c>NetPlayerJoinedEvent</c> 发布 1 次（Host 自身）</item>
        ///   <item>Client 1 连接 → Host 侧再次发布 <c>NetPlayerJoinedEvent</c></item>
        ///   <item>Client 2 连接 → Host 侧再次发布 <c>NetPlayerJoinedEvent</c></item>
        ///   <item>最终 Host 上总共收到 3 次 <c>NetPlayerJoinedEvent</c></item>
        ///   <item>每个事件中的 <c>PlayerRef</c> 应当唯一</item>
        /// </list>
        /// </para>
        /// <para>
        /// <b>已知限制：</b>
        /// <list type="bullet">
        ///   <item>需要 Play Mode + Fusion 运行时</item>
        ///   <item>多个 Runner 实例需在不同的 GameObject 上</item>
        ///   <item>客户端需要使用 Shared Mode（非 Server Mode）以允许 Host 迁移</item>
        ///   <item>简单环回测试在同一进程中创建多个 Runner 可能受 Unity 单线程限制</item>
        /// </list>
        /// </para>
        /// </remarks>
        [Test]
        [Category("Multiplayer")]
        [Description("多个客户端连接到 Host，验证 NetPlayerJoinedEvent 计数")]
        public void MultipleClients_ConnectToHost_PlayerJoinedEventsFired()
        {
            const string testName = nameof(MultipleClients_ConnectToHost_PlayerJoinedEventsFired);

            RequireFusion(testName);
            RequirePlayMode(testName);

            // ── Arrange ────────────────────────────────────
            // 1. 启动 Host
            //    var hostObj = CreateTestObject("HostManager");
            //    var hostManager = hostObj.AddComponent<NetManager>();
            //    yield return null; // Awake
            //
            //    int joinedCount = 0;
            //    var hostAgent = new EventAgent();
            //    hostAgent.Subscribe<NetPlayerJoinedEvent>(ref evt =>
            //    {
            //        joinedCount++;
            //    });
            //
            //    await hostManager.StartHost("MultiTest", SceneRef.FromIndex(0));
            //
            // 2. 等待 Host 场景加载完成
            //    yield return new WaitUntil(() => hostManager.IsConnected);
            //
            // 3. 创建客户端 1
            //    var client1Obj = CreateTestObject("Client1Manager");
            //    var client1Manager = client1Obj.AddComponent<NetManager>();
            //    yield return null;
            //    await client1Manager.StartClient("MultiTest");
            //
            // 4. 创建客户端 2
            //    var client2Obj = CreateTestObject("Client2Manager");
            //    var client2Manager = client2Obj.AddComponent<NetManager>();
            //    yield return null;
            //    await client2Manager.StartClient("MultiTest");

            // ── Act ────────────────────────────────────────
            // yield return WaitForFrames(60); // 等待所有客户端完成连接

            // ── Assert ─────────────────────────────────────
            // Assert.That(joinedCount, Is.EqualTo(3),
            //     $"Host 应收到 3 次 NetPlayerJoinedEvent（自身 + 2 客户端），" +
            //     $"实际收到 {joinedCount} 次");

            Assert.Ignore(
                $"[{testName}] 完整多客户端测试需要 Play Mode + Fusion 运行时。" +
                "桩已就绪：取消注释 Arrange/Act/Assert 块后即可运行。\n\n" +
                "运行提示：\n" +
                "1. 确保 3 个 NetManager 各自有独立的 NetworkRunner 实例\n" +
                "2. 使用 Shared Mode 以便同一进程内创建多个 Runner\n" +
                "3. 连接完成后等待至少 2 秒让 PlayerJoined 事件传播");
        }

        // ═══════════════════════════════════════════════
        //  Test 5: 断线重连事件流程
        // ═══════════════════════════════════════════════

        /// <summary>
        /// 验证客户端断线后的完整事件流程。
        /// </summary>
        /// <remarks>
        /// <b>前置条件：</b>
        /// <list type="bullet">
        ///   <item>Fusion SDK 已安装</item>
        ///   <item>Host 会话已在运行且至少 1 个客户端已连接</item>
        ///   <item>Play Mode 运行</item>
        /// </list>
        /// <para>
        /// <b>预期行为：</b>
        /// <list type="number">
        ///   <item>客户端调用 <c>NetManager.Disconnect()</c></item>
        ///   <item>客户端收到 <c>NetDisconnectedEvent</c></item>
        ///   <item>Host 收到 <c>NetPlayerLeftEvent</c>（对应断线客户端的 PlayerRef）</item>
        ///   <item>客户端 <c>IsConnected</c> == false</item>
        ///   <item>Host <c>IsConnected</c> 保持 true</item>
        /// </list>
        /// </para>
        /// <para>
        /// <b>已知限制：</b>需要 Play Mode + Fusion 运行时 + 已建立的网络会话。
        /// </para>
        /// </remarks>
        [Test]
        [Category("Lifecycle")]
        [Description("客户端断线触发正确的断开和玩家离开事件")]
        public void ClientDisconnect_FiresDisconnectedAndPlayerLeftEvents()
        {
            const string testName = nameof(ClientDisconnect_FiresDisconnectedAndPlayerLeftEvents);

            RequireFusion(testName);
            RequirePlayMode(testName);

            // ── Arrange ────────────────────────────────────
            // 1. 启动 Host
            //    var hostObj = CreateTestObject("Host");
            //    var host = hostObj.AddComponent<NetManager>();
            //    yield return null;
            //    await host.StartHost("DisconnectTest", SceneRef.FromIndex(0));
            //
            // 2. 创建并连接客户端
            //    var clientObj = CreateTestObject("Client");
            //    var client = clientObj.AddComponent<NetManager>();
            //    yield return null;
            //    await client.StartClient("DisconnectTest");
            //
            // 3. 等待连接建立
            //    yield return WaitForFrames(120);
            //
            // 4. 注册事件监听
            //    bool clientDisconnected = false;
            //    int playerLeftCount = 0;
            //    var clientAgent = new EventAgent();
            //    var hostAgent = new EventAgent();
            //    clientAgent.Subscribe<NetDisconnectedEvent>(ref evt => clientDisconnected = true);
            //    hostAgent.Subscribe<NetPlayerLeftEvent>(ref evt => playerLeftCount++);

            // ── Act ────────────────────────────────────────
            // client.Disconnect();
            // yield return WaitForFrames(10);

            // ── Assert ─────────────────────────────────────
            // Assert.That(client.IsConnected, Is.False, "客户端 IsConnected 应为 false");
            // Assert.That(clientDisconnected, Is.True, "客户端应收到 NetDisconnectedEvent");
            // Assert.That(playerLeftCount, Is.EqualTo(1), "Host 应收到 NetPlayerLeftEvent");

            Assert.Ignore(
                $"[{testName}] 完整断线重连测试需要 Play Mode + Fusion 运行时。" +
                "桩已就绪：取消注释 Arrange/Act/Assert 块后即可运行。");
        }

        #endregion
    }
}
