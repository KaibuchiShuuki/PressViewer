# PressViewer 开发与发布约定

这个文件是给后续维护者（包括 AI 助手）看的提醒清单。

## 数据与日志位置

- 程序的数据目录是**程序目录下的 `data/`**（例如 `D:\soft\PressViewer\app\data`），不再写 `%APPDATA%`。
- 里面包含 `key_data.db`、`click_data.db`、`gamepad_data.db`、`app_usage.db`、`appsettings.json`、`logs/`。
- 电脑使用统计由系统服务 `XAssistant.UsageTracker` 产出，数据在 `D:\soft\PressViewer\service\data\`。

## 上传 GitHub 前必须确认

1. **不要提交任何个人数据**：排除 `app/data/`、`service/data/`、`*.db`、`*.db-wal`、`*.db-shm`、`*.bak`、`logs/`。
2. 仓库里只放 `src/`、`assets/`（logo 与图标）、README、CHANGELOG、LICENSE、构建脚本。
3. 提交前跑一次：

   ```powershell
   git status --short
   ```

   确认没有数据库、日志、截图混进来。

4. 截图与临时发布目录（`*-publish*`、`pv-*`、`q-*` 等）不要放进仓库。

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

- 主题色 12 套（四字命名，热力色带随主题与深浅色切换）、深色模式、千分位与小按键缩写开关都在 `Services/ThemeService.cs` 与 `Models/AppSettings.Appearance`。
- 自绘控件（键盘、鼠标、手柄）通过 `ThemeService.Changed` 重绘；文字颜色用 `ThemeService.HeatText(bg)` 自动取黑/白。

## 发布前自检

- 首页、热力图、鼠标/键盘/手柄记录页、应用日志、设置、关于都能打开；
- 深色模式下没有白底残留、文字可读；
- 切换主题色后，键盘图例、首页胶囊、键帽颜色都跟着变；
- 最大化窗口后键盘热图放大。
