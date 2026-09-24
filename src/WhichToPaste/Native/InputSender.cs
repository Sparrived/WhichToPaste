namespace WhichToPaste.Native;

/// <summary>把选中的历史条目粘到当前焦点窗口。</summary>
internal static class InputSender
{
    /// <summary>
    /// 注入按键的签名，放在 dwExtraInfo 里。钩子只放行带签名的注入事件，
    /// 于是宏键盘、AutoHotkey 这类第三方注入的 Ctrl+V 仍按普通按键处理。
    /// </summary>
    public static readonly IntPtr Signature = new(0x57545031); // 'WTP1'

    public static void SendPaste()
    {
        Win32.SendKey((ushort)VK.CONTROL, keyUp: false, Signature);
        Win32.SendKey((ushort)VK.KEY_V, keyUp: false, Signature);
        Win32.SendKey((ushort)VK.KEY_V, keyUp: true, Signature);
        Win32.SendKey((ushort)VK.CONTROL, keyUp: true, Signature);
    }
}