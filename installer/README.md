# installer —— LTFC 安装包（MSI）

`build-msi.ps1` 把仓库里的发布包 `dist\` 打成一个标准 Windows 安装包（MSI）：

* 每机器安装（perMachine），默认装到 `C:\Program Files\LTFC`，安装目录结构与发布包 `dist` 完全一致；
* 安装界面是 WiX 的功能树（中文），可勾选内置模块与桌面快捷方式；
* **默认覆盖安装旧的独立 ClassTell**：安装 LTFC 时 Windows Installer 会先把旧包卸载掉，
  再由 `modules\ClassTell` 里的内置模块接管，用户不需要手动卸载，也不会出现两套 ClassTell。

## 一、一次性准备

```powershell
dotnet tool install -g wix --version 5.0.2                  # 命令行工具（wix.exe）
wix extension add -g WixToolset.UI.wixext/5.0.2             # 安装界面（功能树）
wix extension add -g WixToolset.Util.wixext/5.0.2           # CloseApplication（覆盖前请程序退出）
```

WiX 刻意固定在 5.x：WiX 6/7 需要接受 Open Source Maintenance Fee EULA，本项目不使用它。

## 二、构建

```powershell
# 1) 先出发布包（dist）与 MSI
powershell -ExecutionPolicy Bypass -File build.ps1              # 根目录脚本：编译 + 自测 + 发布 + 生成 MSI

# 2) 或者只重建 MSI（用现有的 dist）
powershell -ExecutionPolicy Bypass -File installer\build-msi.ps1
powershell -ExecutionPolicy Bypass -File installer\build-msi.ps1 -Build            # 先跑一次根 build.ps1
powershell -ExecutionPolicy Bypass -File installer\build-msi.ps1 -IncludeVideoDownload   # 连视频下载一起打包（约 +800 MB）
powershell -ExecutionPolicy Bypass -File installer\build-msi.ps1 -OutputDir release -SkipValidate
```

产物：`release\LTFC-1.0.2_r.msi`（MSI 内部的 Version 必须是纯数字 `1.0.2`，文件名上的 `_r` 只是发行标记）。

脚本会依次：检查 WiX 与扩展 → 清空 `installer\.stage` → 把 `dist` 暂存进去（去掉 `.pdb`，默认不含视频下载）
→ 自检（必需文件、`LTFC.exe` 文件版本是否等于 `-Version`、有没有多带东西）→ 由 `license.txt` 生成 `license.rtf`
→ `wix build` → 校验（ICE + 体积/新鲜度 + 直接读 MSI 表核对安装目录、文件清单、Upgrade 表、卸载清理项）。

### 安装/卸载验证（需要管理员）

安装 LTFC 是每机器安装（会写 `C:\Program Files`），所以**必须在“以管理员身份运行”的 PowerShell 里**做这一步：

```powershell
powershell -ExecutionPolicy Bypass -File installer\test-msi.ps1
powershell -ExecutionPolicy Bypass -File installer\test-msi.ps1 -Msi release\LTFC-1.0.2_r.msi -KeepInstalled
```

它会：记录安装前的 LTFC / ClassTell 条目 → 静默安装 → 核对安装目录里的每个必需文件（含 `modules\ClassTell\ClassTell.exe`）、
`AutoNotice\command.txt` 有没有被误装进 Program Files、旧版独立 ClassTell 是否已被卸载 → 静默卸载 →
核对安装目录是否删除、控制面板条目是否消失、`HKCU\...\Run\LTFC` 是否被清理，最后打印“通过/失败”清单
（日志写到 `%TEMP%\ltfc-msi-test-<时间戳>\`）。它不会运行 `LTFC.exe`，不会动个人配置。

> 在未提权的会话里跑安装会失败（`msiexec` 返回 1603，日志里能看到
> `Note: 1: 1402 ... HKLM\Software\Microsoft\Windows\CurrentVersion\Installer\Rollback\Scripts`，
> 即无权限写 HKLM）——这是权限问题，不是安装包问题。请用管理员 PowerShell 或直接双击 MSI（走 UAC）。

## 三、安装包做了什么

| 项目 | 说明 |
| --- | --- |
| 安装目录 | `C:\Program Files\LTFC`（`INSTALLFOLDER`，安装界面里不可改：模块路径依赖这个结构） |
| 主程序（必需） | `LTFC.exe`、`LTFC.Core.dll`、`LTFC.exe.config`、`assets\`（图标、赞赏码）、`许可（摘要）.txt`、开始菜单与卸载入口、运行期集成 |
| ClassTell 模块 | `modules\ClassTell\`（ClassTell.exe + 配置 + 文档，默认勾选） |
| OpenChoose 模块 | `modules\OpenChoose\`（PPTChooser.exe + 配置，默认勾选） |
| 桌面快捷方式 | 默认勾选，可在功能树里取消 |
| 视频下载 | 只有构建时加 `-IncludeVideoDownload` 才会出现在功能树里（`VideoDownload\`，约 800 MB） |
| 升级 | `MajorUpgrade`：新版本 MSI 覆盖安装本产品；安装前自动关闭运行中的 `LTFC.exe` / `ClassTell.exe` |
| 卸载 | 清掉 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\LTFC` 自启动项；`%APPDATA%\LTFC`、`%APPDATA%\ClassTell` 个人数据保留 |

安装：双击 MSI，或 `msiexec /i "release\LTFC-1.0.2_r.msi"`；静默安装：追加 `/qn`。
每机器安装需要管理员权限（UAC 会提示），这是为了写 `C:\Program Files`。

## 四、为什么能“覆盖安装原来的 ClassTell”

判据来自本机注册表（实际装过 ClassTell 1.0.0 的机器）：

* `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\UserData\S-1-5-18\Products` 里，
  “ClassTell 邮箱指令通知 1.0.0”的产品代码是 `{B7C7C7CB-61A6-4F08-A7CF-5C6073AB55DE}`；
* `HKLM\SOFTWARE\Classes\Installer\UpgradeCodes` 里，这个产品对应的 **UpgradeCode 是
  `{7C9A4E1B-2D3F-4A55-9E6C-1B0F5D2A7C33}`**（也就是 `src\ClassTell\installer\ClassTell.wxs` 里写的那个）。

所以 `LTFC.wxs` 里放了一条使用该 UpgradeCode 的 `<Upgrade>` 记录：

```xml
<Upgrade Id="7c9a4e1b-2d3f-4a55-9e6c-1b0f5d2a7c33">
  <UpgradeVersion Minimum="0.0.0" Maximum="1.0.0" IncludeMinimum="yes" IncludeMaximum="yes"
                  Property="OLD_CLASSTELL_FOUND" MigrateFeatures="no" IgnoreRemoveFailure="no" />
</Upgrade>
```

Windows Installer 因此把旧 ClassTell 当作“被升级产品”，与 `MajorUpgrade` 共用同一个
`RemoveExistingProducts` 动作，顺序是：关闭在跑的程序 → 卸载旧 ClassTell → 安装 LTFC（含内置 ClassTell 模块）。

想改行为时：

* **不想接管旧 ClassTell**（让两者并存）：注释/删掉这段 `<Upgrade>`。
* **以后 ClassTell 出了 1.0.1 之类的新独立包也归 LTFC 管**：把 `Maximum` 调大（例如 `2.0.0`）。
* 以后 LTFC 自己发新版：改 `Package/@Version` 即可，`MajorUpgrade` 负责覆盖旧 LTFC；
  `AllowSameVersionUpgrades="yes"` 让同版本也能直接覆盖重装。

## 五、AutoNotice 的 command.txt 为什么不装到安装目录

`AutoNotice` 是 LTFC 的内部模块（没有独立 exe），它的配置文件需要**用户可写**：
LTFC 的「总设置 → AutoNotice → 打开配置文件」是直接用记事本打开
`AppPaths.AutoNoticeConfigFile` 并让用户就地编辑保存的。而 `C:\Program Files\LTFC` 对普通用户只读，
所以安装包刻意不把 `dist\AutoNotice\command.txt` 装进去。程序第一次用到这个配置时会自己
在 `%APPDATA%\LTFC\AutoNotice\command.txt` 生成同款示例模板（`AutoNoticeConfig.Template()`），
那里随时可写、可编辑。

## 六、常见问题

* **`wix build` 提示“生成的 MSI 比暂存的 LTFC.exe 还旧”或输出文件被占用**
  这是 `wix msi validate` / `msiexec` 留下的 Windows Installer 服务句柄。以管理员身份重启
  “Windows Installer”服务（或重启电脑、或换 `-OutputDir`）后重建即可。脚本会主动挡掉这种“假成功”。
* **安装完 LTFC 提示缺少 ClassTell.exe / PPTChooser.exe**
  说明打进去的目录结构不对。用 `-SkipValidate:$false`（默认）跑一次，脚本会打印 MSI 里
  解析出来的安装目录树，应该看到 `ProgramFiles64Folder\LTFC\modules\ClassTell` 这一支。
* **安装后 AutoNotice 显示没有提醒**
  正常：配置文件在 `%APPDATA%\LTFC\AutoNotice\command.txt`。打开一次
  「AutoNotice → 设置」再点“重新读取”，或直接编辑该文件。
* **中文乱码**
  `Package` 上固定了 `Language="2052"`、`Codepage="936"`；许可页文本由 `license.txt` 生成的
  `license.rtf`（ASCII + `\u` 转义）提供，任何区域设置下都能正常显示。
* **安装包太大**
  默认不含 800 MB 的视频下载；需要“完整版”时加 `-IncludeVideoDownload`。
* **`wix msi validate` 报 `ICE61 ... The Maximum version is not less than the current product`**
  预期警告：`MajorUpgrade` + `AllowSameVersionUpgrades="yes"` 必须让 Upgrade 记录的 Maximum 等于当前版本，
  才能实现“同版本也能覆盖重装”。构建脚本会把它标为预期，不影响安装。
* **改了版本号要同步的地方（4 处）**
  `src\LTFC\LTFC.Core\Ui\Theme.cs` 的 `VersionText`、`src\LTFC\LTFC.App\LTFC.App.csproj` 的
  `Version`、`LTFC.wxs` 的 `Package/@Version`、`build-msi.ps1` 的 `-Version`。
  脚本会比对 `LTFC.exe` 的文件版本，不一致直接报错，避免打出“装了却显示旧版本”的包。

## 七、文件说明

| 文件 | 说明 |
| --- | --- |
| `LTFC.wxs` | 安装包定义（WiX v4/5 schema）：产品信息、安装目录、功能树、快捷方式、升级/卸载、`Files Include` 通配符 |
| `build-msi.ps1` | 构建与校验脚本（暂存 → license.rtf → wix build → 读 MSI 表核对） |
| `test-msi.ps1` | 管理员权限下的安装/卸载验证（静默装 → 核对文件与旧 ClassTell 接管 → 静默卸 → 核对清理） |
| `license.txt` | 许可摘要（UTF-8 源文本） |
| `license.rtf` | 由脚本生成，供安装界面的许可页显示（不需要手工维护） |
| `.stage\`、`release\` | 暂存目录与产物目录（构建时生成，可随时删除） |
