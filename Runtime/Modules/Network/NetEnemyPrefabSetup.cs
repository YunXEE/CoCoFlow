using UnityEngine;
using UnityEngine.AI;
using Fusion;
using CoCoFlow.Runtime.Gameplay.Enemy;
using CoCoFlow.Runtime.Modules.Network.Enemy;

namespace CoCoFlow.Runtime.Modules.Network
{
    /// <summary>
    /// NetEnemy 预制体配置助手。
    /// 挂载到空 GameObject 后在 Inspector 中 Reset，即可自动添加 NetEnemy 预制体所需的全部组件。
    /// 此脚本仅用于 Editor 阶段的预制体创建辅助，运行时不执行任何逻辑。
    ///
    /// <para><b>最终预制体必须包含的组件清单（按添加顺序）：</b></para>
    /// <list type="number">
    /// <item><b>NetworkObject</b> (Fusion) — 网络对象根组件，所有 NetworkBehaviour 的宿主</item>
    /// <item><b>EnemyController</b> (CoCoFlow.Enemy) — AI 大脑，管理 Enemy HFSM 状态机与巡逻/追击逻辑</item>
    /// <item><b>NavMeshAgent</b> (Unity) — 寻路组件，EnemyController 依赖此组件进行路径规划</item>
    /// <item><b>Animator</b> (Unity) — 动画播放，需配置 Avatar 与 AnimatorController</item>
    /// <item><b>NetworkMecanimAnimator</b> (Fusion) — 跨网络同步 Animator 全部参数</item>
    /// <item><b>NetworkTransform</b> (Fusion) — 同步 Transform（位置/旋转），代理端做平滑插值</item>
    /// <item><b>NetEnemyController</b> (NetworkBehaviour) — 网络权威控制，Host 独占 AI 计算，客户端仅接收同步</item>
    /// </list>
    ///
    /// <para><b>创建步骤：</b></para>
    /// <para>1. 创建空 GameObject，命名为 NetEnemy</para>
    /// <para>2. 挂载此脚本</para>
    /// <para>3. 在 Inspector 中右键 → Reset（或 Add Component 时自动触发）</para>
    /// <para>4. 配置 NavMeshAgent 参数（Speed、StoppingDistance 等）</para>
    /// <para>5. 所有组件添加完毕后，可移除此脚本或将 GameObject 拖入 Project 窗口创建 .prefab</para>
    /// </summary>
    [HelpURL("https://github.com/yunxee/CoCoFlow/wiki/Prefab-Setup#netenemy")]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(EnemyController))]
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(NetworkMecanimAnimator))]
    public class NetEnemyPrefabSetup : MonoBehaviour
    {
        #region Editor Setup

        /// <summary>
        /// Editor 中 Reset 时自动添加所有缺失的组件。已存在的组件不会重复添加。
        /// </summary>
        private void Reset()
        {
            AddIfMissing<NetworkObject>();
            AddIfMissing<EnemyController>();
            AddIfMissing<NavMeshAgent>();
            AddIfMissing<Animator>();
            AddIfMissing<NetworkMecanimAnimator>();
            AddIfMissing<NetworkTransform>();
            AddIfMissing<NetEnemyController>();
        }

        #endregion

        #region Internal Logic

        private void AddIfMissing<T>() where T : Component
        {
            if (GetComponent<T>() == null)
                gameObject.AddComponent<T>();
        }

        #endregion
    }
}
