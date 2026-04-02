namespace CoCoFlow.Runtime.Modules.UI
{
    public enum UILayer
    {
        Scene,      // 场景UI (如怪物血条，世界坐标转屏幕坐标)
        HUD,        // 常驻界面 (玩家血条，快捷栏，摇杆，永远在底层)
        Panel,      // 常规面板 (背包，设置，全屏/半屏，会遮挡HUD)
        Popup,      // 弹窗 (确认框，警告框)
        Top         // 顶层 (Loading界面，系统级断线提示)
    }
}