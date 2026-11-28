# PressViewer 开发与发布约定

这个文件是给后续维护者（包括 AI 助手）看的提醒清单。

## 数据与日志位置

- 程序的数据目录是**程序目录下的 `data/`**（例如 `D:\soft\PressViewer\app\data`），不再写 `%APPDATA%`。
- 里面包含 `key_data.db`、`click_data.db`、`gamepad_data.db`、`app_usage.db`、`appsettings.json`、`logs/`。
- 电脑使用统计由系统服务 `XAssistant.UsageTracker` 产出，数据在 `D:\soft\PressViewer\service\data\`。

### 三个按键库的表结构

每个库都有「明细 + 按天汇总 + 压缩标记」三张表：

- `KeyPressRecords(Key, PressTime)` / `ClickRecords(Button, ClickTime)` / `GamepadPressRecords(Button, PressTime)`：每条按键一行，时间用 `DateTime.ToString("o")`。
- `KeyPressDaily` / `ClickDaily` / `GamepadPressDaily`：`(键, Day, Count)`，`Day` 是 `yyyy-MM-dd`，主键 `(键, Day)`。
- `CompactedDays(Day, CompactedAt)`：已压缩过的日期，用来跳过重复扫描。

统计查询必须把两张表合起来算（明细 `COUNT(*)` + 汇总 `SUM(Count)`），`KeyDatabaseService.GetKeyCounts`、`ClickDatabaseService.GetClickCounts`、`GamepadDatabaseService.GetPressCounts` 都是这么写的。按天分组一律用 `substr(PressTime, 1, 10)`（字符串前缀就是本地日期），不要用 SQLite 的 `date()`——ISO 8601 带时区偏移时它按 UTC 折算，会和界面上的「今天」错开。

压缩入口是设置页「数据」卡的按钮，实现见 `Services/DataCompactionService.cs`：只处理今天之前的日期，压完 VACUUM 回收空间。

## 运行权限（1.1.2 起）

- `app.manifest` 里写的是 `requestedExecutionLevel level="requireAdministrator"`。**不要改回 asInvoker**：低层键鼠钩子（`WH_KEYBOARD_LL` / `WH_MOUSE_LL`）受 UIPI 限制，普通权限进程收不到更高权限进程的输入，而《绝区零》《原神》这类游戏的可执行文件自身就是 `requireAdministrator`，不提权就完全记录不到游戏内的按键。
- 因为主程序要管理员权限，登录计划任务必须用**最高权限**（`StartupService.RunLevelHighest = 1`）。若任务仍是普通权限，登录拉起时会被 UAC 拦住，表现为"开机后没有程序在跑"。`StartupService.EnsureHighestPrivilegeTask()` 会在启动时检查并原地升级旧任务。
- 手动双击 exe 会弹一次 UAC，这是提权的固有代价；走计划任务自启不弹。
- **桌面快捷方式不带 UAC 盾徽的办法**：`SettingsViewModel.CreateDesktopShortcut` 把快捷方式的目标写成 `powershell.exe`（`-WindowStyle Hidden`），参数是 `Start-Process -FilePath '<exe>' -Verb RunAs`，图标仍取 `exe,0`。Explorer 的盾徽是按"目标文件的 manifest"叠的，目标不是提权 exe 就不会画盾。**不要**图省事把 TargetPath 直接写成 PressViewer.exe，那样盾徽会回来。
- 排查"游戏里记录不到"时先看 exe 的清单：`[Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($exe))` 里搜 `requestedExecutionLevel`。

## 上传 GitHub 前必须确认

1. **不要提交任何个人数据**：排除 `app/data/`、`service/data/`、`*.db`、`*.db-wal`、`*.db-shm`、`*.bak`、`logs/`。
2. 仓库里只放 `src/`、`assets/`（logo 与图标）、README、CHANGELOG、LICENSE、构建脚本。
3. 提交前跑一次：

   ```powershell
   git status --short
   ```

   确认没有数据库、日志、截图混进来。

4. 截图、设计稿与临时发布目录不要放进仓库：`designs/`、`*-shot.png`、`*-publish*/`、`pv-*`、`q-*` 都已写进 `.gitignore`，本地留着方便对照，但不入库。
5. 发布二进制走 GitHub Releases：直接跑 `tools\release.ps1`（publish 主程序与服务 → 打包 `PressViewer-<版本>-win-x64.zip` → 创建 Release 并上传），脚本从 `git credential fill` 取 token，正文按 UTF-8 字节数组提交，不会有中文乱码。也可以手工打包，规则是：从 `app/` 取，排除 `data/` 与 `*.pdb`，zip 内顶层是 `PressViewer/` 目录；上传后回读 release 确认中文正文没有乱码。
6. 发布前先退出正在运行的 PressViewer，否则 `app/PressViewer.dll` 被占用，publish 会失败（程序以管理员权限运行，普通权限结束不掉）。

## 构建与发布

```powershell
$env:DOTNET_ROOT='D:\soft\dotnet10'
& 'D:\soft\dotnet10\dotnet.exe' publish 'D:\soft\PressViewer\src\PressViewer.csproj' `
  -c Release -r win-x64 --self-contained true `
  -p:IncludeLocalModules=false -o 'D:\soft\PressViewer\app'
```

- 主程序 `net10.0-windows`，同时启用 WPF 与 WinForms，因此 `Color`、`UserControl`、`Point`、`Rectangle`、`Binding`、`Timer` 等类型需要 `using` 别名消歧。
- 服务项目 `src/XAssistant.Service` 被主项目排除，要单独 `dotnet publish` 到 `service/`；服务名仍是 `XAssistant.UsageTracker`，改名会破坏已安装的服务。
- `XAML` 里 `Converter`、`Style` 必须用 `StaticResource`；只有画刷/颜色/字体等可冻结资源才用 `DynamicResource`。主题切换依赖 `ThemeService.Apply` 直接替换 `*Brush` 资源（`SolidColorBrush.Color` 上的 DynamicResource 在 Freezable 上不生效）。

## 主题

- 主题色 12 套（四字命名，热力色带随主题与深浅色切换）在 `Services/ThemeService.cs` 的 `Palettes`，**第一项就是默认主题（当前为薄荷汽水 MintSoda）**。`ThemeService.CurrentTheme` 的初值、`ThemeService.Apply` 的兜底值、`Models/AppSettings.Appearance.Theme` 的默认值三处要保持一致。
- 自绘控件（键盘、鼠标、手柄）通过 `ThemeService.Changed` 重绘；文字颜色用 `ThemeService.HeatText(bg)` 自动取黑/白。
- 「显示千分位分隔符」「小按键用 K / M 缩写」两个开关已在 1.1.1 移除：`NumberFormatService` 只剩 `Format(int/long)`，一律输出带千分位的完整数字。

## 图标与标志

- `Assets/logo.png`：程序内标志与 README 头图，1254×1254。**保留原图的纯黑底**——抠成透明会让键盘缝、手柄轮廓这些深色细节一起消失，浅色模式下反而看不清；侧边栏用 `helpers:CornerClip.Radius` 给黑底压成圆角，避免方块边界太硬。
- `Assets/app-icon.ico`：窗口 / 任务栏 / 托盘图标，由同一张 logo 生成 16、24、32、48、64、128、256 七个尺寸，每帧用 PNG 压缩，同样保留黑底。
- 换图标时两个文件一起换，PNG 保持正方形，ICO 必须带 256 尺寸，否则大图标视图会糊。

## 手柄热力图样式（备用方案记录）

- 手柄热力图有**两套画法**，都在 `Views/GamepadShapeView.xaml.cs` 里，由 `Services/GamepadLayoutService.cs` 的 `Current` 决定：
  - `Tile` —— 键帽式：无机身轮廓，按键排成大键帽分区（左右两列肩键、左摇杆 + 十字、中间 Back/Start、右侧 ABXY 菱形 + 右摇杆）。**1.1.1 起固定使用这一套**，设置页已删掉切换开关。
  - `Form` —— 手柄形：保留 `BodyData` 手柄轮廓（斜向渐变 + 两侧握把暗部 + 上沿柔光），按键按真实手柄位置摆放。代码完整保留（`FormLayout()`、机身绘制、rim 路径），只是没有入口。
- 要恢复手柄形：把 `GamepadLayoutService.Current` 的初值改成 `Form`（或调用 `GamepadLayoutService.Set(GamepadLayoutService.Form)`），设置页无需改动即可显示；两种样式的按键集合与数字排版逻辑共用，加新按键只需在 `FormLayout()` / `TileLayout()` 里各加一行 `PadKey`。
- `AppSettings.Appearance.GamepadLayout` 字段保留给以后恢复用，当前启动不再读取它。

## 发布前自检

- 首页、热力图、鼠标/键盘/手柄记录页、应用日志、设置、关于都能打开；
- 深色模式下没有白底残留、文字可读；
- 切换主题色后，键盘图例、首页胶囊、键帽颜色都跟着变；
- 最大化窗口后键盘热图放大。
