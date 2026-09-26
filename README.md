# WhichToPaste

复制即记录，粘贴时先挑一条再粘。

按下粘贴键（默认 `Ctrl+V`）时，鼠标指针旁边弹出一个选择框，按"新到旧"列出最近复制过的内容；
再按一次 `Ctrl+V` 就把选中的那条粘到光标处。选择框全程不抢焦点，原输入框的插入符与选区不会丢。

支持三类剪贴板内容：**文本、文件、图片**。截图、从资源管理器复制的文件都能进历史，重新粘回去。

## 运行要求

- Windows 10 / 11（x64），已安装 **.NET 10 Desktop Runtime**
- 或用自包含发布，单文件免运行时（见下）

## 构建与运行

```powershell
# 编译整个解决方案（主程序 + 自检）
dotnet build WhichToPaste.slnx

# 直接运行
dotnet run --project src/WhichToPaste/WhichToPaste.csproj
```

### 发布

```powershell
# 框架依赖单文件：0.24 MB，需要目标机装 .NET 10 Desktop Runtime
dotnet publish src/WhichToPaste/WhichToPaste.csproj -c Release -r win-x64 `
  -p:SelfContained=false -p:PublishSingleFile=true

# 自包含，无需任何运行时：exe 140 MB + 6 个 WPF 原生库，共约 148 MB
dotnet publish src/WhichToPaste/WhichToPaste.csproj -c Release -r win-x64 `
  -p:SelfContained=true -p:PublishSingleFile=true

# 自包含且只要一个文件：约 148 MB，启动时会先解包到临时目录
dotnet publish src/WhichToPaste/WhichToPaste.csproj -c Release -r win-x64 `
  -p:SelfContained=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

产物在 `src/WhichToPaste/bin/Release/net10.0-windows/win-x64/publish/`。
注意必须用 `-p:SelfContained=...` 这种写法；`dotnet publish --self-contained false` 会被忽略，
结果仍然是自包含（140 MB）。

## 用法

| 操作 | 默认快捷键 | 说明 |
| --- | --- | --- |
| 弹出选择框 | `Ctrl+V` | 历史非空时拦截；历史为空则维持系统默认粘贴 |
| 上移一项 | `Ctrl+C` | 与粘贴键相邻，单手够得着 |
| 下移一项 | `Ctrl+B` | |
| 粘贴选中项 | `Ctrl+V` | 选择框已弹出时，再按一次即为确认 |
| 关闭选择框 | `Esc` / `Ctrl+Z` | |

- 选择框弹出时点框外任意位置也会收起，并让那次点击照常传给原窗口。
- 在输入框上**右键**也能直接弹出选择框（可在设置里关掉，关掉后右键仍是系统菜单）。
- 快捷键、右键接管、暂停监听、保留条数、开机自启、浅/深色外观都在设置页里改。
- 托盘图标：左键打开设置，右键菜单可打开设置 / 暂停监听 / 退出。首次运行会自动亮出设置页。

## 数据与配置

默认都在 `%APPDATA%\WhichToPaste\`：

| 文件 | 内容 |
| --- | --- |
| `settings.json` | 全部配置 |
| `history.json` | 历史条目（图片只存文件名，内容在 `images/`） |
| `images/*.png` | 图片条目，文件名是内容哈希，同一张图不会存两份 |
| `error.log` | 只在出现未处理异常时写入 |

设了环境变量 `WHICTOPASTE_HOME` 就改用该目录，可用于便携部署（把数据放在程序旁边）。
历史超出保留条数后自动丢弃最旧的记录，对应的图片文件一并清理。

## 自检

```powershell
dotnet build WhichToPaste.slnx
tests/WhichToPaste.Tests/bin/Debug/net10.0-windows/WhichToPaste.Tests.exe
```

覆盖快捷键解析与匹配、条目去重键与显示语义、历史去重/裁剪/落盘重载、图片编码落盘与回收、选择框摆放。
自检会把自己指到临时目录，不会碰真实数据；有失败时退出码为 1。

## 代码结构

```
src/WhichToPaste/
  Native/     Win32 互操作：全局钩子、可编辑目标探测、按键注入、剪贴板监听
  Services/   设置、历史、图片落盘、快捷键模型、开机自启
  Models/     条目模型（文本 / 文件 / 图片）
  UI/         Material 3 主题、选择框、设置页、托盘图标
  AppController.cs  把上面几块串起来，所有回调回到 UI 线程
tests/WhichToPaste.Tests/  不依赖测试框架的自检
```

几个关键取舍：

- **不用第三方依赖**：WPF 画界面，托盘借 WinForms 的 `NotifyIcon`，其余全是直接 P/Invoke。
- **选择框永不激活**（`WS_EX_NOACTIVATE` + `WS_EX_TOOLWINDOW`）：键盘操作全走全局钩子转发，
  这样原输入框的插入符、选区、输入法状态都不会被打断。
- **注入的按键带签名**（`dwExtraInfo`）：钩子只放行自己注入的 `Ctrl+V`，不会自触发；
  宏键盘、AutoHotkey 这类第三方注入的按键仍按普通按键处理。
- **定位用物理像素**（`SetWindowPos`）：绕开 WPF 的 DIP 映射，多屏与不同缩放比例下都能贴住指针。
- **弹窗贴指针并躲着指针**：以鼠标指针为基准摆在下方 / 上方，再夹进该显示器的工作区保证整框可见。
  指针始终落在卡片之外的 6px 空隙里，所以刚弹出来时鼠标不会指着它，一动或一点都不会误选；
  只有贴边被夹回来仍压住指针（框比工作区还高）时才换到指针另一侧。
  摆放计算单独放在 `UI/PickerPlacement.cs`，纯矩形运算，可脱离界面自检。

## 已知限制

- 只记录**文本、文件、图片**三类。HTML/RTF、Excel 单元格等自定义剪贴板格式不记录——
  这类格式无法保证还被目标程序接受，记下来多半是死条目。同时给了文本和位图的内容
  （如 Excel）按文本记录。
- 右键接管依赖 UI Automation 判断目标是否可编辑，探测带 400ms 超时；判不出来时
  退回系统右键菜单（不会误吞）。
- 历史不支持搜索、置顶、分组；托盘菜单用的是 WinForms 原生样式。
- 图片以 PNG 落盘，不压缩画质也不去重相似图。
