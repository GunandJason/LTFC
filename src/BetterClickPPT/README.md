# PPT 触摸增强工具（BetterClickPPT）

> 一款面向触摸屏 / 触摸一体机的 **PPT 放映增强工具**：在触摸屏上放 PPT 时，
> **用手指轻点一下屏幕就能翻到下一页**，不必再滑动。
> 同时兼容 **Microsoft PowerPoint** 与 **WPS 演示**，安装时自动查找它们的安装位置，并让你自己选择要为哪个软件启用。
>
> **名称说明**：界面标题、托盘提示、快捷方式与“已安装的应用”里显示的名字是 **PPT 触摸增强工具**
> （旧版本叫“BetterClickPPT 触控点击翻页增强器”，升级安装会自动清理旧快捷方式，不会出现两个图标）；
> 安装目录、进程与数据目录仍沿用 `BetterClickPPT`，因此升级 / 卸载不受影响。

---

## 1. 它解决什么问题

在触屏一体机 / 触摸屏笔记本上放映时，很多环境会出现这种情况：

- 用鼠标（或翻页笔）点一下能翻页，但**手指点屏幕没有反应**；
- 只能靠**手指滑动**才能切换下一页，讲课时很不顺手；
- PowerPoint 的“触摸模式”把单击当成了手势，WPS 也有类似行为。

本程序常驻系统托盘，会识别“正在全屏放映的演示文稿窗口”，
并把 **触摸 / 手写笔的轻触** 直接转换成 **下一页** 按键：

| 输入 | 默认行为 |
| --- | --- |
| 手指轻触（单指、快速、位移小） | **还原成一次真实鼠标点击**（等价于用鼠标左键点在同一位置）：原本要在页内播放的**分步动画**会逐帧推进，**超链接**、屏幕上的**“上一页 / 下一页”按钮**也都能正常点中；若该位置没有任何可点对象，就是翻到下一页 |
| 手指滑动 / 双指缩放 / 长按 | **保持原样**，交给 PowerPoint / WPS 自己处理（滑动仍可回到上一页） |
| 鼠标左键点击 | **保持原样**（原本就能翻页） |
| 编辑 PPT 时的任何点击 | **完全不受影响**（只增强“全屏 + 无边框”的放映窗口） |
| 笔记本电脑触摸板 | 忽略（避免误触） |

> 轻触的处理方式可在“设置 → 翻页行为 → 轻触的处理方式”中切换：
> **模拟鼠标点击**（默认，功能最完整）或 **发送下一页按键**（只翻页，不触发页内动画与超链接）。

---

## 2. 工作原理（为什么既能兼容 PPT 又能兼容 WPS）

真正的“插件”形态（VBA / COM 加载项）无法在 PowerPoint 与 WPS 之间通用，
而且很多情况下触摸输入在应用内部就被当成手势消费掉了。因此本项目采用
**操作系统级输入增强** 的架构，对两个软件一视同仁：

```
BetterClickPPT.exe（托盘常驻）
├── 放映窗口识别（SlideshowDetector）
│     · 进程名规则：POWERPNT.EXE / wpp.exe …（可在设置中自定义）
│     · 窗口类名规则：PowerPoint 全屏放映窗口类名 = screenClass
│     · 几何规则：窗口必须铺满显示器且没有标题栏（避免把最大化的编辑窗口误判）
│
├── 通道 A：全局低层鼠标钩子（WH_MOUSE_LL）
│     · Windows 会把触摸 / 笔输入“提升”为鼠标消息，并在 dwExtraInfo 中写入特征值 0xFF515700
│     · 识别到“触摸来源的点击”且落在放映窗口上 → 由“动作窗口”执行处理动作，并可拦截原始点击
│
└── 通道 B：原始输入（Raw Input / RIDEV_INPUTSINK）
      · 某些程序（例如 PowerPoint 触摸模式）自行消费触摸输入，系统不再提升为鼠标消息
      · 此时直接监听触摸屏 HID 报告（Tip Switch / Contact Count / X / Y），
        用状态机判定“单指 + 短时 + 位移小”的轻触，并按显示器尺寸把触点换算回屏幕坐标
```

两条通道共用同一个“翻页闸门”（防抖 + 去重），因此**一次轻触只会触发一次动作**。

### 钩子只做决策，动作在自己的消息循环里执行（很重要）

低层鼠标钩子回调里**绝不能**再调用 `SendInput`：注入出来的点击又需要本进程的钩子去处理，
而钩子回调还没返回，会自我死锁并卡住整个桌面的输入。因此实现上：

- 钩子回调只做“是否属于放映窗口、是否吞掉”的快速决策；
- 真正的动作（把轻触还原成鼠标点击、或发送下一页按键）通过一个隐藏的“动作窗口”
  投递到程序自己的消息循环里执行。

### 轻触的两种处理方式

| 模式 | 行为 | 适用 |
| --- | --- | --- |
| **模拟鼠标点击**（默认） | 在触点位置合成一次真实左键点击（并吞掉原始触摸点击以避免重复） | 动画、超链接、屏幕上的上一页/下一页按钮、触摸模式下被忽略的单击——全部与鼠标一致 |
| 发送下一页按键 | 直接发送 `PageDown`（放映窗口不在前台时改为投递窗口消息） | 只想要“点哪都翻页”的场景 |

### 放映开始时自动切回鼠标模式

放映前如果 PowerPoint / WPS 处于“**触摸模式**”（页面上的按钮、工具栏明显变大，单击被当成手势），
即使轻触被正确还原成鼠标点击，屏幕上可点的对象也会变少。因此默认开启
「**放映开始时自动切回鼠标模式**」：

- 程序识别到**进入全屏放映**时，在后台把放映窗口的「触摸 / 鼠标模式」按钮切回**鼠标模式**；
- **只对 PowerPoint / WPS 演示生效**（内置规则，或进程名为 `POWERPNT.EXE` / `wpp.exe` / `kwpp.exe` 等）；
  其它放映软件（用户自定义的规则）完全不动，也不会留下“未能切换”的无用日志；
- 通过 Office 自己的 **UI Automation** 按钮完成，**不注入键鼠、不激活窗口**，
  因此不会打断放映、也不会让放映窗口失去响应；
- 放映结束会取消未完成的尝试；关闭该开关后，程序绝不改动 Office 的任何设置。

> 关闭开关：设置 →「翻页行为」→ 取消勾选「放映开始时自动切回鼠标模式」。

### 高 DPI / 多显示器适配

- 程序按 **PerMonitorV2（每显示器 DPI 感知）** 运行，界面按 DPI 自动缩放；
- 钩子坐标在某些环境下是“物理像素”而窗口 API 返回“逻辑像素”，
  因此匹配坐标时会按系统 DPI 比例**双向换算兜底**，避免“点不准”；
- 合成鼠标点击时使用 `MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK`，
  以整个虚拟桌面（多显示器）为基准换算坐标；
- “忽略边缘”等判断基于**放映窗口所在显示器**的范围，而不是整块虚拟屏幕。

> 选择“发送下一页按键”时，按键的发送方式由程序自动选择：
> 放映窗口在前台 → **SendInput 模拟真实按键**；否则 → **PostMessage 直接投递到放映窗口**，
> 这样即使放映窗口不在前台（演讲者视图、多显示器、被遮挡）也能可靠翻页，且按键不会跑到别的程序里。

---

## 3. 安装（傻瓜式，无需管理员权限）

1. 把 **`dist\BetterClickPPT-Setup.exe`** 发给使用者，双击运行。
2. 向导会自动查找本机安装位置（按顺序尝试）：
   - 注册表 `App Paths\POWERPNT.EXE` / `wpp.exe` 等；
   - COM 注册信息（`PowerPoint.Application`、`KWPP.Application` 等 → CLSID → LocalServer32）；
   - Office ClickToRun 安装信息、`Office\16.0\PowerPoint\InstallRoot`；
   - WPS 的 `InstallRoot` / `Kingsoft\Office\...` 注册表与常见目录；
   - 卸载列表（DisplayName 含 PowerPoint / WPS）；
   - 正在运行的进程路径；
   - 依然找不到时，可以点「浏览…」手动指定主程序。
3. 在“选择目标软件”页勾选要为哪个软件启用（**已安装的默认勾选**），并可按需修改主程序路径。
4. 选择安装位置（默认 `%LOCALAPPDATA%\Programs\BetterClickPPT`，不需要管理员权限）、
   是否开机自启、是否创建桌面快捷方式。
5. 完成。程序会常驻托盘（右下角），**打开 PPT / WPS 按 F5 放映，手指轻点屏幕即翻页**。

安装程序同时会把卸载信息写入“设置 → 应用 → 已安装的应用”，可随时正常卸载。

### 命令行安装 / 静默部署（供批量部署或自动化使用）

```powershell
# 静默安装：为 PowerPoint 与 WPS 都启用，静默、不弹窗
BetterClickPPT.exe --install --ppt --wps --quiet

# 只给 PowerPoint 启用，自定义安装目录
BetterClickPPT.exe --install --ppt --no-wps --dir "D:\Tools\BetterClickPPT"

# 静默卸载（会删除设置与日志）
BetterClickPPT.exe --uninstall --quiet
```

---

## 4. 使用与设置

- **托盘图标（双击）** → 打开设置窗口：
  - **目标软件**：启用/停用某个软件、查看已检测到的主程序路径、按进程名添加“自定义放映程序”；
  - **翻页行为**：触摸翻页开关、是否吞掉原始点击、**轻触的处理方式**（模拟鼠标点击 / 发送下一页按键）、
    **放映开始时自动切回鼠标模式**、深度触控模式、翻页按键、发送方式、防抖间隔、忽略屏幕边缘像素；
  - **诊断**：一键检测当前前台窗口（是否被识别为放映窗口）、查看触摸设备、实时活动日志、
    打开日志文件夹与配置文件。
- **托盘右键**：暂停/启用、设置、检测当前前台窗口、打开日志文件夹、打开配置文件、关于、退出。
- 进入放映时会有气泡提示（可在设置中关闭）；退出放映不再弹窗，只在托盘图标提示文本里体现状态。

### 有问题时的排查顺序

1. 托盘右键 →「检测当前前台窗口」：应显示 **匹配结果：命中规则「Microsoft PowerPoint」**；
   若显示“不匹配”，请把窗口信息发给开发者（或把进程名/类名加进自定义规则）。
2. 命令行执行 `BetterClickPPT.exe --detect`：输出环境检测报告（安装状态、检测到的软件、
   触摸屏设备、当前所有放映规则）。
3. 命令行执行 `BetterClickPPT.exe --probe`：列出 PowerPoint / WPS 的顶层窗口
   （类名、矩形、是否全屏、是否有标题栏）以及是否命中规则。
4. 常见结论：
   - **点屏幕没反应**：在设置里打开「深度触控模式」（直接监听触摸屏 HID 报告）；
   - **点一下触发了两次**：保持「吞掉触摸原始点击」为勾选状态；
   - **点一下直接跳到下一页、页内动画没播放 / 超链接点不开 / 上一页按钮点不到**：
     把「轻触的处理方式」设为 **模拟鼠标点击**（默认值即为此），它会把轻触原样还原成鼠标左键点击；
   - **在触摸屏上想回到上一页**：用滑动（保持原样），或轻触屏幕上的“上一页”按钮（模拟鼠标点击模式下可用）；
   - **完全没反应且 --probe 显示“未命中”**：放映窗口类名/进程名较特殊，添加自定义规则即可。

---

## 5. 构建、测试与验证

```powershell
# 一键构建：编译 → 自测 → 发布单文件 exe 到 dist\
powershell -ExecutionPolicy Bypass -File build\build.ps1

# 跳过自测
powershell -ExecutionPolicy Bypass -File build\build.ps1 -SkipTests

# 自测程序（不依赖任何测试框架）
tests\BetterClickPPT.SelfTest\bin\Release\net8.0-windows\BetterClickPPT.SelfTest.exe
tests\BetterClickPPT.SelfTest\bin\Release\net8.0-windows\BetterClickPPT.SelfTest.exe --unit-only
tests\BetterClickPPT.SelfTest\bin\Release\net8.0-windows\BetterClickPPT.SelfTest.exe --e2e-only --no-install

# 真实 PowerPoint 端到端验证（会短暂全屏放映一个自动生成的演示文稿）
powershell -ExecutionPolicy Bypass -File tools\verify-powerpoint.ps1 -AppPath dist\BetterClickPPT.exe

# 安装向导（GUI）自动化验证：用 UI Automation 真实点完 欢迎 → 选择目标软件 → 选项 → 安装 → 完成
powershell -ExecutionPolicy Bypass -File tools\verify-setup-wizard.ps1

# 设置窗口（GUI）自动化验证：检查三个标签页的关键控件与“轻触的处理方式”默认值
powershell -ExecutionPolicy Bypass -File tools\verify-settings-ui.ps1
```

`build.ps1` 产物：

| 文件 | 说明 |
| --- | --- |
| `dist\BetterClickPPT-Setup.exe` | **分发给用户的安装程序**（同时也是程序本体，约 66MB，自包含，无需预装 .NET） |
| `dist\BetterClickPPT.exe` | 同一文件（安装后复制到安装目录的就是它） |
| `dist\lite\BetterClickPPT.exe` | 框架依赖版（约 400KB，需要安装 .NET 8 桌面运行时） |

### 自测覆盖内容（33 项 = 单元 23 + 端到端 9 + 安装 / 卸载 1）

- 单元测试（23 项）：通配符匹配、配置读写与容错收敛、轻触动作默认值与克隆、
  鼠标点击坐标换算（含多显示器 / 高 DPI / 边界夹紧）、**动作窗口（验证钩子外的动作投递机制）**、
  放映窗口规则匹配（含“最大化的编辑窗口不误判”）、触摸/鼠标来源判定、
  轻触状态机（轻触 / 长按 / 滑动 / 多指 / 丢事件恢复）、按键映射、目标软件检测、深度触控启动；
  以及「放映开始时自动切回鼠标模式」共 10 项：配置开关的默认值 / 克隆 / 往返读写、增强器接线、
  守护逻辑（关闭或目标无效不动作、先失败后重试成功、超过次数上限后放弃、切换实现抛异常不影响主流程、
  放映结束取消未完成的尝试）、**只对 PowerPoint / WPS 放映窗口执行切换**、模式判定与选中项推断、
  「触摸 / 鼠标模式」按钮的只读探测（进程号无效时安全返回、绝不抛异常）。
- 端到端测试（9 项，真实全局钩子 + 真实输入注入 + 独立进程的模拟放映窗口，类名 `screenClass`）：
  触摸轻触→下一页且原始点击被吞掉、防抖（连点只触发一次）、关闭吞掉后点击放行、
  窗口消息方式翻页、不匹配窗口完全不受影响、真实全屏窗口识别、
  普通注入点击被忽略、**触摸轻触还原为鼠标点击且位置精确**、**左侧轻触位置保持（上一页按钮可用）**。
- 安装测试：临时目录下的「安装 → 重复安装（升级）→ 卸载」全流程，
  校验注册表、配置文件、快捷方式、自启项与清理结果（测试前后会备份/恢复用户配置）。

> 端到端测试需要真实的输入注入；若桌面输入队列被其它程序长期占用（`SendInput` 被阻塞），
> 这 9 项会以“**跳过**”而不是“卡死”的方式给出明确提示，其余测试照常运行。
> 同理，`tools\verify-powerpoint.ps1` 会先做一次带超时的注入自检，不可注入时直接给出结论并退出。

- 真实 PowerPoint 验证脚本（`tools\verify-powerpoint.ps1`）覆盖：
  触摸轻触只前进 1 页（下一页按键模式，含前台模拟按键与窗口消息两条路径）、
  普通鼠标点击不受影响、**分步动画逐帧推进、动画播完才翻页、轻触超链接被原样还原**
  （即“模拟鼠标点击”模式的完整行为验证）。
- 安装向导验证脚本（`tools\verify-setup-wizard.ps1`）用 UI Automation 真实点击安装向导，
  14 项全部通过：自动检测到 PowerPoint 并默认勾选、完成页显示安装成功、
  安装目录生成主程序、卸载后目录与快捷方式清理干净。
- 设置窗口验证脚本（`tools\verify-settings-ui.ps1`）用 UI Automation 打开设置窗口并逐个切换标签页，
  16 项全部通过：目标软件 / 翻页行为 / 诊断三个页面控件齐全，
  **“轻触的处理方式”默认值为“模拟鼠标点击”**，且 250% 缩放下布局正常。

### 本项目实测结果（开发机：Windows 11 + Office 16，250% DPI）

| 验证 | 结果 |
| --- | --- |
| `BetterClickPPT.SelfTest.exe --unit-only`（23 项单元） | 23 / 23 通过（含「放映开始时自动切回鼠标模式」10 项） |
| `BetterClickPPT.SelfTest.exe`（安装 → 升级 → 卸载，第 33 项） | 通过（临时目录，测试后自动还原用户配置） |
| `BetterClickPPT.SelfTest.exe`（端到端 9 项） | 见下方说明 |
| `tools\verify-powerpoint.ps1`（真实 PowerPoint 放映） | 见下方说明 |
| `tools\verify-setup-wizard.ps1`（安装向导 GUI 自动化） | 14 / 14 通过（含 250% 缩放下的界面与流程） |
| `tools\verify-settings-ui.ps1`（设置窗口 GUI 自动化） | 16 / 16 通过（含新“轻触的处理方式”默认值） |

> **关于端到端验证的环境说明**：本次改动过程中，旧版本里“在钩子回调中调用 `SendInput`”的问题
> 让一个测试进程死锁在系统调用里（详见第 2 节“钩子只做决策”），而该进程持有的全局鼠标钩子
> 使整台机器的输入注入被占住、且普通权限无法结束它。因此这台机器上：
> 单元测试 + 安装测试 + 安装向导验证均为 **全绿**；
> 9 项端到端测试与真实 PowerPoint 脚本会“**明确跳过并提示原因**”，
> 重启（或结束当前 VS Code / 终端会话）释放后，直接执行下面两条命令即可完成这部分验证：
>
> ```powershell
> powershell -ExecutionPolicy Bypass -File build\build.ps1
> powershell -ExecutionPolicy Bypass -File tools\verify-powerpoint.ps1 -AppPath dist\BetterClickPPT.exe
> ```
>
> 在此之前（改动前的那一版）这 9 项端到端 + PowerPoint 10 项均为全绿；
> 本次的修复已通过单元测试（坐标换算、动作投递机制）与代码审查确认。
>
> **「放映开始时自动切回鼠标模式」的验证状态**：
> - 单元测试 10 项全绿（含“只对 Office 放映窗口执行切换”“按钮只读探测”）；
> - 真机只读复核（PowerPoint 16.0.10417，**只读**探测、不点击）：放映窗口 `PPTFrameClass` 下
>   确实存在「触摸 / 鼠标模式」按钮（`NetUIAnchor`），且能读到它的选中状态
>   （`ExpandCollapsePattern`）→ “在后台切回鼠标模式”的识别依据成立；
> - “真机放映时自动切回”的端到端确认：在干净会话里执行 `tools\verify-powerpoint.ps1` 时观察日志
>   （设置 →「诊断」→ 实时活动日志，或 `%LOCALAPPDATA%\BetterClickPPT\betterclickppt.log`）：
>   成功时写入 `已自动把 Office 切回「鼠标模式」（触摸模式 → 鼠标模式…）`；
>   本来就已是鼠标模式时写入 `Office 已经是「鼠标模式」，无需切换`；
>   非 Office 的放映软件只写一条 `非 Office 放映窗口（xxx.exe）：跳过「自动切回鼠标模式」`。
> - WPS 路径（`wpp.exe` / `kwpp.exe` / `wpspresent.exe`）需在装有 WPS 的机器上按同样方式确认。

---

## 6. 项目结构

```
BetterClickPPT/
├─ BetterClickPPT.sln
├─ Directory.Build.props             公共编译属性（版本、可空、语言版本…）
├─ assets\BetterClickPPT.ico         应用图标（由 tools\make-icon.ps1 程序化生成）
├─ build\build.ps1                   一键构建 / 测试 / 发布
├─ dist\                             发布产物（安装程序 exe）
├─ docs\使用说明.md                   面向使用者的简明说明
├─ tools\
│   ├─ make-icon.ps1                 生成多尺寸 .ico（无需外部素材）
│   ├─ BcppInjector.cs               验证脚本用的点击注入工具
│   └─ verify-powerpoint.ps1         真实 PowerPoint 端到端验证
├─ src\BetterClickPPT.Core\          核心引擎（纯 Win32 互操作，可单测）
│   ├─ Config\                       AppConfig / TargetRule / ConfigStore / 内置规则
│   ├─ Engine\SlideshowEnhancer.cs   编排：识别 + 两条输入通道 + 翻页闸门
│   ├─ Input\                        鼠标钩子、原始输入触摸、轻触状态机、鼠标点击还原、按键发送、DPI 辅助
│   ├─ Interop\                      user32 / hid.dll 导入、隐藏消息窗口（动作在消息循环执行）
│   ├─ Slideshow\                    窗口探测、规则匹配、诊断
│   └─ Deployment\                   软件检测、安装/卸载、快捷方式、自启、注册表
├─ src\BetterClickPPT.App\           界面程序（安装向导 + 托盘 + 设置 + 诊断）
│   ├─ Setup\                        安装向导（欢迎 / 选择目标软件 / 选项 / 进度 / 完成）
│   ├─ Tray\                         托盘程序与设置窗口
│   ├─ Ui\                           图标绘制、进度窗口、输入框
│   └─ CliRunner*.cs                 命令行模式（--detect/--probe/--install/--uninstall…）
└─ tests\
    ├─ BetterClickPPT.MockShow\      独立进程的“模拟放映窗口”（类名 screenClass）
    └─ BetterClickPPT.SelfTest\      自测程序（单元 + 端到端 + 安装）
```

---

## 7. 安装足迹与卸载

| 位置 | 内容 |
| --- | --- |
| `%LOCALAPPDATA%\Programs\BetterClickPPT\BetterClickPPT.exe` | 程序本体（单文件） |
| `%LOCALAPPDATA%\BetterClickPPT\config.json` | 设置 |
| `%LOCALAPPDATA%\BetterClickPPT\logs\betterclickppt.log` | 日志（自动滚动，默认上限 512KB） |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\BetterClickPPT` | 开机自启（可在设置中关闭） |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\BetterClickPPT` | 卸载信息（“应用和功能”中可见） |
| 开始菜单 /（可选）桌面快捷方式 | 指向程序本体 |

卸载方式：开始菜单快捷方式右键、或“设置 → 应用 → 已安装的应用”，
也可以执行 `BetterClickPPT.exe --uninstall`；卸载会一并删除设置与日志。

---

## 8. 已知限制

- 目标程序若以**管理员权限**运行（高完整性级别），普通权限的程序无法向它发送按键，
  此时可对本程序右键“以管理员身份运行”（安装本身仍不需要管理员权限）。
- “吞掉触摸原始点击”会同时阻止**手指点击超链接**；如需要该功能，请在设置中关闭它。
- 笔记本触摸板被有意忽略（避免误触）；只处理触摸屏与手写笔。
- WPS 各版本窗口类名不完全相同，因此 WPS 规则使用“进程名 + 全屏 + 无边框”判定，
  必要时可在设置中添加自定义进程名/类名。
- 深度触控模式依赖触摸屏驱动正确上报 HID 报告；若驱动异常，可用 `--detect` 报告查看设备与解析状态。
- 开发机上若无触摸屏，深度触控通道无法在自测中实测（自测覆盖的是“触摸被提升为鼠标点击”的通道）；
  该通道的解析与判定逻辑已用合成数据做了单元测试。


