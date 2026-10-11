# OpenChoose（PPTChooser）

双击 `.ppt` / `.pptx` 时弹出选择窗口，自己决定用 **PowerPoint / WPS / LibreOffice / 其它程序** 打开。

* 程序名：`OpenChoose`
* 可执行文件：`PPTChooser.exe`（基于 **.NET Framework 4.5**）
* 安装包：`OpenChoose.msi`（**每用户安装，不需要管理员权限**）
* 开机自启动：安装后在后台托盘运行，随时可以打开选择窗口或调整设置

```
双击 .ppt/.pptx  ->  Windows 文件关联  ->  PPTChooser.exe "文件.pptx"
                 ->  弹出“选择打开 PPT 的程序”
                 ->  用户选择 PowerPoint / WPS / 其它程序
                 ->  以  xxx.exe "文件.pptx"  启动被选程序
```

选择窗口：

```
┌─────────────────────────────────┐
│      选择打开 PPT 的程序        │
├─────────────────────────────────┤
│ 文件：xxx.pptx                  │
│                                 │
│  PowerPoint                     │
│  WPS Office                     │
│  LibreOffice Impress            │
│  选择其他程序...                │
│                                 │
│  使用 Windows“打开方式”对话框  设为默认打开方式
│  当前默认打开方式：Microsoft PowerPoint
│                    [打开] [取消] │
└─────────────────────────────────┘
```

* 「记住我的选择，以后直接用该程序打开」这一栏**已隐藏**：窗口里不再显示该复选框，其下方内容整体上移一行。

## 目录结构

```
OpenChoose\
├─ build.ps1                     一键构建（图标 -> exe -> msi -> 自检）
├─ install.ps1                   不依赖 MSI 的备用安装 / 卸载（同样无需管理员）
├─ README.md
├─ src\PPTChooser\               程序源码（C# / WinForms，目标框架 net45）
│   ├─ PPTChooser.csproj
│   ├─ app.manifest              以普通用户权限运行 + DPI 感知
│   ├─ Program.cs                入口与命令行解析
│   ├─ MainForm.cs / .Designer.cs 选择窗口
│   ├─ TrayApp.cs                托盘（开机自启动时的后台程序）
│   ├─ ProgramFinder.cs          检测 PowerPoint / WPS / LibreOffice
│   ├─ Launcher.cs               以 xxx.exe "文件.pptx" 启动程序
│   ├─ FileAssociation.cs        文件关联注册（HKCU）
│   ├─ DefaultProgramHelper.cs   设为默认打开方式
│   ├─ StartupManager.cs         开机自启动（HKCU\...\Run）
│   ├─ AppConfig.cs / Log.cs / Ui.cs / Diagnostics.cs
│   └─ Resources\app.ico         应用图标（由 tools\make-icon.ps1 生成）
├─ installer\
│   ├─ Product.wxs               WiX 5 安装包定义（per-user、中文向导、自动关闭运行中的程序）
│   ├─ License.rtf               安装向导里的许可说明（由 tools\make-license-rtf.ps1 生成）
│   └─ WixUIBannerBmp.bmp / WixUIDialogBmp.bmp   向导横幅图（由 tools\make-banner.ps1 生成）
├─ tools\
│   ├─ make-icon.ps1             生成鼠标指针图标 app.ico（多尺寸：BMP≤64 + PNG 128/256）
│   ├─ make-banner.ps1           生成安装向导横幅（493x58 / 493x312 BMP）
│   ├─ make-license-rtf.ps1      生成 installer\License.rtf（中文，\uN 转义）
│   ├─ icon-preview.ps1          把图标各尺寸导出 PNG，便于肉眼检查
│   ├─ test-smoke.ps1            冒烟测试（关联/自启动/窗口/DPI/启动参数）
│   ├─ check-msi.ps1             安装 / 卸载验证
│   ├─ msi-wordcount.ps1         检查 MSI 摘要信息（是否要求管理员权限等）
│   ├─ ArgEcho.cs                测试用参数回显程序
│   ├─ WinList.cs                测试用窗口枚举（含真实像素尺寸）
│   ├─ ShootWindow.cs            测试用窗口截图（PrintWindow，DPI 精确）
│   ├─ dpi-aware.manifest        测试辅助程序的 DPI 感知清单
│   └─ check-state.ps1           开发期注册表/窗口状态检查
└─ dist\                         构建产物（PPTChooser.exe、OpenChoose.msi、日志、截图）
```

## 构建

依赖：

| 依赖 | 说明 |
| --- | --- |
| .NET SDK 8（或任意含 MSBuild 的环境） | 编译 `net45` 程序；`Microsoft.NETFramework.ReferenceAssemblies` 会从 NuGet 还原 4.5 参考程序集 |
| WiX Toolset 5 | 打包 MSI：`dotnet tool install --global wix --version 5.*` |

```powershell
cd OpenChoose
powershell -ExecutionPolicy Bypass -File build.ps1          # 构建
powershell -ExecutionPolicy Bypass -File build.ps1 -Test    # 构建并跑测试
```

产物：

* `src\PPTChooser\bin\Release\PPTChooser.exe`
* `dist\OpenChoose.msi`

> 说明：`Product.wxs` 里的相对路径是按 “installer 目录” 解析的，所以 WiX 必须在
> `installer` 目录下执行（`build.ps1` 已经处理好）。
>
> 首次构建会自动安装两个 WiX 扩展（`WixToolset.UI.wixext` 安装向导、`WixToolset.Util.wixext`
> 关闭运行中的程序），并生成向导素材（中文许可说明 + 鼠标指针横幅图），因此首次构建需要联网。
> 向导语言由 `-culture zh-CN` 指定（WiX 自带中文 UI 字符串）。

## 安装 / 卸载

```powershell
msiexec /i dist\OpenChoose.msi                # 图形化安装向导（无需管理员）
msiexec /i dist\OpenChoose.msi /qn            # 静默安装
msiexec /x dist\OpenChoose.msi /qn            # 静默卸载（也可用“设置 → 应用”）
```

安装包特点：

* **每用户安装、不需要管理员权限**（`Scope="perUser"`，只写 `HKCU` 与 `%LOCALAPPDATA%`）。
  可用 `tools\msi-wordcount.ps1` 复核：摘要信息的 Word Count 应为 `10`（= 压缩包 + 支持每用户/LUA 安装，
  bit3 = 每用户安装能力，**不是**“需要管理员”），且 `ALLUSERS` 未定义；
  安装日志里会有 `MSI_LUA: Package is marked as LUA installation capable with no elevation required`。
* **中文安装向导**（`-culture zh-CN`）：产品名、许可说明、按钮均为中文，左侧横幅是本程序的鼠标指针图标。
* **再次运行同一个 MSI** 会进入“修复 / 删除”界面（而不是静默重装），不会让人误以为“装了没反应”。
* **自动关闭正在运行的程序**：安装/升级/卸载时若托盘或选择窗口仍在运行，会先友好关闭、2 秒后强制结束，
  避免“文件正在使用”导致安装失败（WiX `util:CloseApplication`）。
* **自动生成安装日志**：`MsiLogging` 已开启，每次安装都会在 `%TEMP%\MSI*.log` 留下日志，便于排查失败原因。

安装内容：

| 项目 | 位置 |
| --- | --- |
| 程序 | `%LOCALAPPDATA%\OpenChoose\PPTChooser.exe` |
| 文件关联、应用注册 | `HKCU\Software\Classes\...`（`OpenChoose.PPT`、`Applications\PPTChooser.exe`、扩展名 `OpenWithProgids` / `OpenWithList` / 右键菜单） |
| 应用功能（默认应用页面） | `HKCU\Software\OpenChoose\Capabilities` + `HKCU\Software\RegisteredApplications` |
| 开机自启动 | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\OpenChoose` = `"...\PPTChooser.exe" --startup` |
| 开始菜单 | `OpenChoose（选择打开 PPT 的程序）`、`设为默认打开方式`、`诊断报告` |
| 卸载 | 控制面板 / 设置 → 应用（名称 OpenChoose） |

卸载会清理程序文件、注册表关联、自启动项与快捷方式（用户配置 `%APPDATA%\OpenChoose\settings.ini` 保留）。

### 不依赖 MSI 的备用安装（同样的效果，仍然不需要管理员）

如果机器禁用/拦截 MSI（组策略、杀软、下载来源被 SmartScreen 拦截等），可以直接用脚本安装：

```powershell
powershell -ExecutionPolicy Bypass -File install.ps1            # 安装（复制 exe、注册关联、自启动、快捷方式、卸载入口）
powershell -ExecutionPolicy Bypass -File install.ps1 -Uninstall # 卸载
powershell -ExecutionPolicy Bypass -File install.ps1 -NoAutostart -NoShortcuts   # 可选：不写自启动 / 不建快捷方式
```

脚本源码、目标路径、注册表项与 MSI 完全一致，并会在 `HKCU\...\Uninstall\OpenChoose` 写一条
“应用和功能”卸载入口，因此两种方式都能正常卸载。

### 安装失败排查

| 现象 | 原因 | 处理 |
| --- | --- | --- |
| 双击 MSI 没反应 | 之前已经装过一次（同一份 MSI 会进入维护/修复流程） | 现在会显示“修复 / 删除”界面；也可直接运行程序，或先卸载再装 |
| 报错 `1925` / `1730`（权限不足） | 账户是标准用户且被要求提权 | 本包不需要管理员；若被组策略强制提权，请改用 `install.ps1` |
| 报错 `1303` / `1304`（文件正被使用） | 托盘或选择窗口仍在运行（旧版本安装包） | 新版会自动关闭；老版本可先在任务管理器结束 `PPTChooser.exe` |
| 报错 `1625` / `1730` | 系统策略禁止安装 MSI | 用 `install.ps1` 备用安装 |
| 提示缺少 .NET Framework 4.5 | 系统确实过旧（Win7 且未装 4.5） | 安装 .NET Framework 4.5+（Win10/11 已自带；安装包会同时检查 32/64 位视图，避免误判） |
| 装完双击 PPT 仍用别的程序打开 | Windows 10/11 的默认应用 `UserChoice` 受保护 | 打开“设为默认打开方式”快捷方式，或到“设置 → 应用 → 默认应用”里手动改一次 |
| 想确认到底失败在哪 | — | 查看 `%TEMP%\MSI*.log`（最后几十行通常有明确错误码） |


## 使用

1. 安装后双击任意 `.ppt` / `.pptx`（Windows 11 需先在“设置 → 应用 → 默认应用”里把 `.pptx`
   关联到 **OpenChoose（PPT 打开方式选择器）**，见下一节）。
2. 弹出 **选择打开 PPT 的程序**：列表自动列出本机安装的 PowerPoint / WPS Office /
   LibreOffice Impress，未安装的标注“（未检测到）”。
3. 选中一项后点 **打开**（或双击该项），程序会以 `xxx.exe "文件.pptx"` 的方式启动它。
4. **“记住我的选择，以后直接用该程序打开”这一栏已隐藏**：窗口里不再显示该复选框，
   界面整体上移一行。是否记住选择完全由配置决定（`%APPDATA%\OpenChoose\settings.ini`
   里的 `RememberChoice=1` 时才记住，默认不记住）；按住 Shift 双击文件可临时强制显示选择窗口。
5. **选择其他程序...**：浏览任意 `.exe`（例如其它演示软件），选中的程序会加入列表。
6. **使用 Windows“打开方式”对话框**：调用系统原生的“打开方式”对话框。
7. **设为默认打开方式**：注册文件关联并尝试把 `.ppt/.pptx` 指向本程序。

托盘菜单（开机自启动后出现在通知区域，双击图标可打开选择窗口）：打开 PPT 文件...、
修复 / 注册文件关联、设为默认打开方式...、清除已记住的选择、开机自动启动（勾选切换）、退出。

## 高 DPI（高分屏 / 缩放）适配

程序声明为 **系统 DPI 感知**（`app.manifest`：`dpiAware=true` + `dpiAwareness=system`），
并按系统缩放比例自动调整界面，实测在 **250% 缩放（240 DPI）** 与 **375%** 下均无错位、无文字裁切：

* 窗口与控件位置/大小由 WinForms 按 DPI 自动缩放（`AutoScaleMode.Dpi`，设计基准 96 DPI）。
* 字体使用“点”为单位，天然随 DPI 放大，不会与控件尺寸脱节。
* 自动缩放**不会**处理的“非尺寸属性”由程序补齐（这是高分屏下最常见的错位原因）：
  * `ListBox.ItemHeight`（列表行高）
  * `ImageList.ImageSize`（列表图标）
  * 自绘时的图标/文字偏移量，并用高质量插值绘制放大后的图标
* 两个链接（“使用 Windows‘打开方式’对话框 / 设为默认打开方式”）按实际文字宽度依次排列，
  字体或缩放变化时不会重叠。
* ```--scale=1.5``` 可在系统 DPI 缩放的基础上再整体放大（见下方参数表），
  用于个别机器上仍觉偏小的场景。
* 图标是**多尺寸 ICO**（16/24/32/48/64 为 BMP 帧，128/256 为 PNG 帧），
  任务栏、托盘、列表、资源管理器大图标下都清晰。

> 说明：不使用 per-monitor DPI（`true/pm`）。net45 版 WinForms 无法处理 `WM_DPICHANGED`，
> 声明 per-monitor 反而会导致高 DPI 下文字与控件错位，因此选择系统级 DPI 感知。

## 关于“默认打开方式”（重要）

* 所有关联都写在 **HKCU**（当前用户），因此安装**不需要管理员权限**，也不影响其它用户。
* Windows 10 / 11 用带哈希校验的 `UserChoice` 保护默认应用，**第三方程序无法直接修改**。因此：
  * 若该扩展名还没有 `UserChoice`，安装程序 / `--setdefault` 会把默认值写为 `OpenChoose.PPT`，双击即刻生效；
  * 若系统已存在 `UserChoice`（例如曾选过“始终使用 PowerPoint 打开”），请到
    **设置 → 应用 → 默认应用** 把 `.ppt` / `.pptx` 改为 **OpenChoose（PPT 打开方式选择器）**；
    `--setdefault` 或窗口里的“设为默认打开方式”会自动引导打开该设置页。
* 即使没有设为默认，安装后在 PPT 文件的 **右键 → 打开方式** 中也能看到
  `OpenChoose（PPT 打开方式选择器）`，并出现“用 OpenChoose 选择打开方式”菜单项。
* 托盘后台运行（`--startup`，LTFC 的“启动模块”也是用它）时会**静默补齐“打开方式”注册**：
  发现关联缺失就自动重写 HKCU 项（`IsRegistered` 不通过时才写），只记日志、不弹窗，
  因此右键“打开方式”里始终能看到 OpenChoose。
* 窗口 / 托盘里点“设为默认打开方式”之前会先弹出**安全软件提醒**：
  这一步要写注册表，360 / 火绒 / 电脑管家 / Windows Defender 等可能弹窗拦截，
  出现提示时请选择“允许 / 信任”。命令行加 `--silent`（安装程序、脚本、`tools\test-smoke.ps1`）
  时不会弹这个提醒，行为与以前完全一致。

## 命令行参数

| 参数 | 说明 |
| --- | --- |
| `PPTChooser.exe "文件.pptx"` | 弹出选择窗口（文件关联就是通过它调用） |
| `--startup` | 开机自启动模式：托盘后台运行（`Run` 项使用） |
| `--register` | 注册文件关联（写入 HKCU，无需管理员） |
| `--unregister` | 取消文件关联并清理注册表项 |
| `--setdefault` | 注册关联并尝试设为 `.ppt/.pptx` 默认打开方式（系统已锁定 `UserChoice` 时不覆盖，只做引导） |
| `--scale=1.5` / `--scale 1.5` | 在系统 DPI 缩放基础上再整体放大（`auto` 或 `0.5 ~ 3.0`；用于高分屏下仍觉偏小） |
| `--enable-autostart` / `--disable-autostart` | 启用 / 关闭开机自启动 |
| `--selftest [报告文件]` | 生成诊断报告（检测到的程序、关联状态、自启动、配置） |
| `--silent` | 与上面配合，不弹任何对话框（供脚本/安装程序调用） |
| `--help` | 显示用法 |

## 测试

```powershell
powershell -ExecutionPolicy Bypass -File tools\test-smoke.ps1   # 冒烟测试
powershell -ExecutionPolicy Bypass -File tools\check-msi.ps1    # 安装/卸载验证
powershell -ExecutionPolicy Bypass -File tools\msi-wordcount.ps1 -Msi dist\OpenChoose.msi
                                                                # 检查 MSI 摘要信息（是否要求管理员权限）
```

安装包相关的手工验证建议（每项都做过）：

1. `msiexec /i dist\OpenChoose.msi`（图形向导，中文）能正常走完；
2. 托盘/选择窗口仍在运行时安装也能成功（会自动关闭它们）；
3. 安装后 `PPTChooser.exe --selftest` 报告正常；
4. `msiexec /x dist\OpenChoose.msi /qn` 卸载后注册表与文件全部清理干净；
5. 备用的 `install.ps1` / `install.ps1 -Uninstall` 效果与 MSI 一致。

* `test-smoke.ps1`：程序图标可加载 → 注册关联 → 设为默认（尊重系统 UserChoice）→ 写入自启动项 →
  `--startup` 托盘运行 → 以 “文件.pptx” 参数启动并检查窗口标题/客户区尺寸 →
  校验进程 DPI 感知为系统级 → `--scale=1.5 / 2.0` 校验布局按比例放大 →
  记住选择后直接启动 `xxx.exe "文件.pptx"`（用 `tools\ArgEcho.cs` 编译的参数回显程序校验，
  路径含空格与中文）→ 最后清理。结果写入 `dist\smoke-test.log`，
  同时保存窗口截图 `dist\shot-native.png`（系统 DPI）与 `dist\shot-scale-150.png`（放大 1.5 倍）。
* `check-msi.ps1`：静默安装 → 校验文件/注册表/快捷方式/ARP/程序自检 → 静默卸载 →
  校验清理干净。结果写入 `dist\msi-check.txt`。

图标预览（可肉眼确认指针图标在各尺寸下的清晰度）：

```powershell
powershell -ExecutionPolicy Bypass -File tools\icon-preview.ps1
# 生成 dist\icon-256.png、dist\icon-zoom-32.png、dist\icon-strip.png
```

程序自检：

```powershell
PPTChooser.exe --selftest C:\temp\openchoose.txt --silent
```

报告内容包括：目标框架（应显示 `.NETFramework,Version=v4.5`）、检测到的 PPT 程序及路径、
各扩展名的生效 ProgID / UserChoice / 打开命令、开机自启动状态、配置文件与日志位置。

## 已知限制 / 注意事项

1. **Windows 10/11 的默认应用保护**：见上文，需用户确认一次。
2. 安装包会把 `.ppt` / `.pptx` 的 HKCU 默认值指向 OpenChoose（系统未设置 `UserChoice` 时生效）；
   卸载时自动删除该值以恢复原状。
3. 托盘程序在**下次登录**才会出现在通知区域；也可从开始菜单启动一次立即使用。
4. 开机自启动仅针对当前用户（`HKCU`）；若在托盘里取消勾选，重新安装/修复会再次写入。
5. 程序检测策略：先查注册表 `App Paths`，再查常见安装路径与版本号目录
   （如 `...\WPS Office\<版本>\office6\wpp.exe`）。若程序装在非常规位置，
   用“选择其他程序...”手动指定（该选择会被记住）。
6. 日志：`%APPDATA%\OpenChoose\log.txt`；配置：`%APPDATA%\OpenChoose\settings.ini`。
