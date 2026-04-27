using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Modules.Network
{
    // 网络连接状态改变事件
    public struct NetworkStateEvent
    {
        public string StateMessage; // 比如："正在连接服务器...", "已进入大厅"
    }

    // 房间列表更新事件
    public struct RoomListUpdateEvent
    {
        // 传递当前所有可用的房间列表
        public System.Collections.Generic.List<Photon.Realtime.RoomInfo> RoomList;
    }

    // Ping 值更新事件 (用于UI显示延迟)
    public struct PingUpdateEvent
    {
        public int Ping; // 延迟毫秒数
    }

    public class NetworkManager : MonoBehaviourPunCallbacks
{
    public static NetworkManager Instance;

    // 缓存房间列表，因为 PUN2 的 OnRoomListUpdate 每次只推送变动的房间
    private Dictionary<string, RoomInfo> _cachedRoomList = new Dictionary<string, RoomInfo>();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        // 自动同步场景：房主加载游戏场景时，所有玩家自动跟着加载
        PhotonNetwork.AutomaticallySyncScene = true;
    }

    // ==========================================
    // 1. 登录与连接
    // ==========================================
    public void ConnectToServer(string playerName)
    {
        PhotonNetwork.NickName = playerName;
        EventBus.Publish(new NetworkStateEvent { StateMessage = "正在连接服务器..." });

        // 如果已经连上，直接去大厅；否则开始连接
        if (PhotonNetwork.IsConnected)
        {
            PhotonNetwork.JoinLobby();
        }
        else
        {
            PhotonNetwork.ConnectUsingSettings();
        }
    }

    public override void OnConnectedToMaster()
    {
        EventBus.Publish(new NetworkStateEvent { StateMessage = "已连接到主服务器，正在进入大厅..." });
        // 连接主服务器成功后，立刻加入大厅才能获取房间列表
        PhotonNetwork.JoinLobby();
    }

    public override void OnJoinedLobby()
    {
        EventBus.Publish(new NetworkStateEvent { StateMessage = "已进入大厅" });
        _cachedRoomList.Clear(); // 进大厅先清空旧列表
    }

    // ==========================================
    // 2. 房间列表逻辑 (核心难点)
    // ==========================================
    public override void OnRoomListUpdate(List<RoomInfo> roomList)
    {
        // 刷新缓存字典
        foreach (RoomInfo info in roomList)
        {
            // 如果房间被关闭、不可见或满员，从列表移除
            if (!info.IsOpen || !info.IsVisible || info.RemovedFromList)
            {
                if (_cachedRoomList.ContainsKey(info.Name))
                {
                    _cachedRoomList.Remove(info.Name);
                }
            }
            else
            {
                // 更新或新增房间信息
                _cachedRoomList[info.Name] = info;
            }
        }

        // 提取字典中的值，打包成事件发给 UI 层
        var currentRooms = new List<RoomInfo>(_cachedRoomList.Values);
        EventBus.Publish(new RoomListUpdateEvent { RoomList = currentRooms });
    }

    // ==========================================
    // 3. 创建与加入房间
    // ==========================================
    public void CreateCustomRoom(string roomName)
    {
        EventBus.Publish(new NetworkStateEvent { StateMessage = "正在创建房间..." });

        RoomOptions options = new RoomOptions();
        options.MaxPlayers = 4; // 设定最大人数

        // 💡 炫技点：PUN2默认没有"房主名字"属性，我们需要用自定义属性塞进去
        ExitGames.Client.Photon.Hashtable customProps = new ExitGames.Client.Photon.Hashtable();
        customProps.Add("Creator", PhotonNetwork.NickName);
        options.CustomRoomProperties = customProps;
        // 必须把这个Key暴露给大厅，大厅的玩家才能看到
        options.CustomRoomPropertiesForLobby = new string[] { "Creator" };

        PhotonNetwork.CreateRoom(roomName, options);
    }

    public void JoinSpecificRoom(string roomName)
    {
        EventBus.Publish(new NetworkStateEvent { StateMessage = "正在加入房间..." });
        PhotonNetwork.JoinRoom(roomName);
    }

    public override void OnJoinedRoom()
    {
        EventBus.Publish(new NetworkStateEvent { StateMessage = "成功加入房间！等候开始..." });

        // 只有房主才有权限加载对战场景
        if (PhotonNetwork.IsMasterClient)
        {
            // 注意：这里使用 PhotonNetwork.LoadLevel 而不是 SceneManager
            // 假设你的战斗场景叫 "GameScene"
            // PhotonNetwork.LoadLevel("GameScene");
        }
    }

    // ==========================================
    // 4. 断线与退出
    // ==========================================
    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        Debug.Log($"玩家 {otherPlayer.NickName} 退出了房间");
        // 这里你也可以发一个事件，让UI提示某某退出了
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        EventBus.Publish(new NetworkStateEvent { StateMessage = $"连接断开: {cause}" });
    }

    // ==========================================
    // 5. 延迟(Ping)监测机制
    // ==========================================
    private float _pingTimer = 0f;
    private void Update()
    {
        // 每秒钟检测一次延迟并广播，避免每帧发送浪费性能
        if (PhotonNetwork.IsConnected)
        {
            _pingTimer += Time.deltaTime;
            if (_pingTimer >= 1.0f)
            {
                _pingTimer = 0f;
                int currentPing = PhotonNetwork.GetPing();
                EventBus.Publish(new PingUpdateEvent { Ping = currentPing });
            }
        }
    }
}

}

