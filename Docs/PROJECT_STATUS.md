# MR Home Assistant

功能与原生 Unity 验证日期：2026-09-22。2026-10-20 已在独立 checkout 复跑 55 项离线协议检查并完成 Runtime / Editor 校验源码编译，详情见[发布前检查](Validation/publication-2026-10-20.md)。
本页的 UI / Play Mode 结果仍为 9 月 22 日的历史记录；当前实现与未完成边界以本页及该检查记录为准。

## 当前阶段

主场景已有可操作 dashboard，本轮完成可靠性与日常设备操作增量。
房间聚合、收藏、最近使用、灯光详情和中文名称已接入实际界面。
实际 Unity 渲染、桌面 Play Mode、Unity 原生 JSON/WebSocket 本地模拟联调已通过。
Quest 真机及真实 Home Assistant 联调尚未完成，不能把桌面验证等同于真机验收。

## 已实现

- 主入口是 `Assets/Scenes/Bootstrap.unity`，已加入 Build Settings。
- Connection 页：地址校验、密码遮罩、连接/取消/断开、清除本机凭证、错误反馈、离线演示入口。
- 断线后保留只读快照并自动恢复，最多重试 5 次，间隔为 2/4/8/16/30 秒；主动断开取消恢复，认证拒绝停止重试。
- 每 15 秒进行 ping/pong 心跳，5 秒未回应视为失联。应用暂停时关闭连接，恢复时重新订阅和同步；不重放旧控制操作。
- 修复旧连接被取消后，其异步收尾干扰新连接恢复状态的竞态。
- Devices 页：名称/entity ID/房间搜索、All/Lights/Switches/Sensors 分类、固定五行分页。
- 读取 HA area/device/entity registries；同设备的多个实体聚合，实体房间设置优先于设备房间，隐藏/禁用实体不展示。注册表权限不足时降级为实体列表。
- 独立房间选择页、收藏和最近使用视图；最近使用只记录成功确认的操作，最多 20 个。收藏按规范化服务器端点隔离，演示偏好不写入正式配置。
- 灯光详情按 `supported_color_modes` 提供亮度/色温，范围来自 HA；滑块先编辑草稿，Apply 只发送实际改过的参数。相关传感器可切换查看，不出现灯光控件。
- 灯、开关与 `input_boolean` 支持开关操作。其他实体只读，不暗示空调、门锁等复杂设备已支持控制。
- 设备操作显示 Updating，等待服务结果及状态回传；拒绝、超时和断线给出反馈，等待期间禁用重复操作。
- 演示使用独立样例状态，始终标记 DEMO；不连接真实设备。
- MR 面板：保留 Meta UISet shell、PointableCanvas、ray/poke 与现有 rig；跟踪就绪后放置在视线前方，支持 Recenter。
- 无 XR 的桌面模式：同一 Canvas 切为屏幕 UI，启用 Input System 鼠标/键盘模块，禁用冲突的 PointableCanvasModule。
- 桌面背景覆盖测试场景画面，避免天空和 XR 射线混入操作界面；XR 模式不启用此背景。
- 面板内文本键盘含大小写、符号、删除、粘贴、Done/Cancel；token 输入始终遮罩。
- 已修复连续输入时光标索引滞后及键盘遮挡导航标签的问题。
- 自定义文字使用 UISet 保色标签，避免默认主题在启动时覆盖主按钮与状态文字的对比色。
- 打包 Noto Sans CJK SC 动态 TMP 回退字体，验证中文名称可见，并修复中英文混排行高导致的提前省略。字体许可证和 SHA-256 位于 `Assets/Resources/Fonts/`。
- 运行时同一帧的设备状态事件合并刷新；连接阶段和用户操作仍及时反馈。尚未宣称大规模设备的真机性能已达标。
- 主场景中的独立 Cube/Poke sample 在运行时隐藏。UISet/UISetPatterns 保持组件参考场景用途。
- Android 已配置网络权限和局域网 HTTP 明文通信支持。远程访问应使用 HTTPS。

## 使用

1. 用 Unity `6000.4.3f1` 打开项目，等待依赖导入。
2. 打开 `Bootstrap.unity`，进入 Play Mode。无需 Home Assistant 即可点 Open demo。
3. 接入真实服务：Connection 页填写服务地址，例如 `http://homeassistant.local:8123`，以及 Home Assistant profile 中创建的 long-lived access token。
4. 点击 Connect。成功后进入 Devices。服务器拒绝 token 时需重新输入。
5. 退出应用后需重新输入 token；应用只记住服务地址。当前会话内保留凭证用于 Reconnect。
6. Quest 输入可点字段或 Edit 打开面板键盘；面板回正可点 Recenter，或用控制器 Start 按钮。桌面可用 Home 键。
7. Devices 页通过 All rooms 选择房间；星标收藏，行末箭头打开详情。最近使用按成功操作排序。亮度/色温修改后点 Apply。
8. 断线时 Cached 状态不可控制；等待自动重连或在 Connection 页取消/重新连接。Forget credentials 清理当前会话凭证及本机旧配置中的 token，不等于在 HA 服务器撤销 token。

旧版 `app-settings.json` 中已有的 token 仍可读取以兼容旧安装；下一次保存配置会去除它。
新的 token 不写入配置、日志或项目文件。不要把真实凭证放进 Inspector、测试 fixture 或截图。

## 代码入口

| 文件 | 职责 |
| --- | --- |
| `Assets/Scripts/Runtime/Presentation/UI/HomeAssistantHudPresenter.cs` | 页面、用户操作、连接反馈、桌面/XR 模式和面板定位 |
| `Assets/Scripts/Runtime/Presentation/UI/DashboardUI.cs` | 统一尺寸、配色、输入框、按钮、开关 |
| `Assets/Scripts/Runtime/Presentation/UI/DashboardKeyboard.cs` | 面板内文本输入与遮罩 |
| `Assets/Scripts/Runtime/Presentation/UI/DashboardDeviceDetails.cs` | 多实体详情、能力驱动滑块及草稿提交 |
| `Assets/Scripts/Runtime/Presentation/UI/DashboardFonts.cs` | 本地中文动态回退字体 |
| `Assets/Scripts/Runtime/Application/HomeAssistant/HomeAssistantConnection.cs` | WebSocket 会话、请求匹配、状态订阅、服务调用与演示隔离 |
| `Assets/Scripts/Runtime/Application/HomeAssistant/HomeAssistantDevices.cs` | 分类、搜索、排序、可操作能力与样例状态 |
| `Assets/Scripts/Runtime/Application/HomeAssistant/HomeAssistantCatalog.cs` | 注册表元数据、房间继承、设备聚合 |
| `Assets/Scripts/Runtime/Infrastructure/Persistence/DashboardPreferencesRepository.cs` | 按服务器隔离的收藏和最近使用持久化，无凭证 |
| `Assets/Editor/HomeAssistantUIValidation.cs` | 临时附加场景 UI 检查与真实 Unity 截图，按独立图层隔离，不保存/覆盖主场景 |
| `Assets/Editor/HomeAssistantPlayModeValidation.cs` | Bootstrap Play Mode 输入回归；禁用测试中的自动连接与配置写入，使用演示设备 |

UI 在运行时构建于现有 UISet Canvas，编辑模式中仍可看到旧 HUD 的序列化对象。
面板使用固定 1000 x 720 参考尺寸，MR 宽度约 0.8 m；桌面按完整面板缩放，避免裁切。
现有 UI 文案和面板键盘保持英文；已验证中文名称显示，但没有实现完整中文本地化或拼音输入法。
收藏与最近使用保存在 `persistentDataPath/dashboard-preferences.json`，不包含 token。
房间元数据在连接时加载；运行期间修改 HA 房间配置后需重新连接，暂未订阅 registry 更新。

## 验证

已完成：

- Runtime C# 与新增 Editor 验证工具通过 Unity 缓存引用的 Roslyn 编译。
- Unity `6000.4.3f1` 原生编译与运行通过。
- 147 项 UI 断言通过，最终时间见 `Validation/unity-ui-checks.txt`：Canvas/ray/poke 组件、控件及滑块边界、分页、分类、房间/收藏/最近使用、多实体详情、中文字体、等待状态、键盘连续输入和密码遮罩。
- 7 张隔离场景截图：连接页、设备页、竖屏设备页、房间页、灯光详情、键盘、断开状态；检查非空白、缺失 shader 像素并目视确认布局。
- 92 项 Bootstrap Play Mode 断言通过，最终时间见 `Validation/unity-playmode-checks.txt`：Input System 模拟鼠标经 EventSystem 命中并操作控件，TMP 文本搜索、键盘提交/取消、收藏/最近/房间筛选、灯光滑块及断开清理、启动后的主题颜色。
  保存 5 张真实 Game view 截图。另用 Unity 原生 JsonUtility 和 WebSocket 对 loopback mock 验证注册表解析、灯光参数调用与回传、暂停/恢复、清除会话；实际确认 Cached 标识可见且控制禁用。
  该项是原生运行时自动化，不是人工实体键盘或 Quest 输入验收。
- 本地测试 55 项通过：URL、配置不落 token、设备聚合/房间继承、偏好隔离与损坏回退、演示能力、UTF-8 分片、
  初始快照期间事件、服务成功/拒绝、无状态确认超时、认证失败、取消竞态、断线只读及自动恢复、无指令重放、暂停恢复、心跳失联、元数据权限降级。
- `git diff --check` 通过。
- Android manifest XML 解析通过。按 GUID 确认 1 个 OVRCameraRig 和 1 个 PointableCanvasModule，interaction rig 引用指向官方 prefab。

UISet 通用静态检查器的两个 rig 检查按对象名匹配，不识别本项目的 `[BuildingBlock]` 前缀，产生误报；
已直接核对脚本/prefab GUID。它还会计入运行时隐藏的独立 ray/poke sample，
以及 prefab 中由 scene override 填充的空 theme 列表；这些警告不替代后续 Unity 验证。

离线复现：

```sh
node Tools/Validation/offline-check.mjs
```

需要 Node、已安装的 Unity 和项目已恢复的 `Library/Bee` 编译引用。可用 `UNITY_CONTENTS` 指定 Unity.app/Contents。
脚本写入被 git 忽略的 `Library/MRHomeAssistantValidation`，测试服务器仅绑定 loopback。
协议测试运行生产连接代码，但用 test adapter 替代 Unity 原生生命周期/JsonUtility，不能替代 Play Mode。

在 Unity 编辑模式执行：

`Tools > MR Home Assistant > Validate UI and Capture`

该工具生成 `Docs/Validation/` 下的截图和 `unity-ui-checks.txt`。
Play Mode 自动化入口为 `Tools > MR Home Assistant > Run Play Mode Validation and Exit`；
它会打开 Bootstrap、执行测试，然后退出 Editor。运行前保存自己的场景改动。
XR 自动启动和后台输入设置仅为本次桌面测试临时调整，结束后恢复，不保存这些测试设置到项目。
实际截图使用图形化 Editor，不能用 `-nographics`；Game view 截图不使用 `-batchmode`。
本轮曾遇到截图纹理跨异步等待被 Editor 资源回收、Canvas 初始导入期间空白渲染，以及一次断言完成后的原生 Enlighten 线程退出崩溃。
截图工具已调整纹理生命周期、限制为三次 Canvas 预热并在通过像素检查后才覆盖截图，退出移至 Editor 回调；最终 UI 复测 147 项通过且进程正常退出。
这是本地复测结果，不代表已经定位或修复 Unity 原生引擎问题。

原生协议联调先运行 `node Tools/Validation/offline-check.mjs --serve`，取得 loopback URL，
再用 Unity CLI 执行 `-executeMethod MRHomeAssistant.Editor.HomeAssistantPlayModeValidation.RunBatch -mrValidationUrl <loopback-url>`。
测试入口只接受 `127.0.0.1`，使用固定的合成测试凭证；测试结束停止 mock 进程。

结果入口：

- [UI 检查报告](Validation/unity-ui-checks.txt)
- [Play Mode 检查报告](Validation/unity-playmode-checks.txt)
- [设备页](Validation/devices.png)、[连接页](Validation/connection.png)、[键盘](Validation/keyboard.png)
- [桌面实际 Game view](Validation/playmode-devices.png)
- [灯光详情](Validation/light-details.png)、[房间选择](Validation/rooms.png)
- [运行时断线只读状态](Validation/playmode-stale.png)

## 下一步验收

1. 构建 Quest APK，检查 ray/poke 命中、初始位置、回正、输入键盘、中文实体名、字体可读性及设备表现。
2. 接入真实 Home Assistant，验证认证、状态同步、一个灯和一个开关的操作、服务器重启与权限拒绝。
3. 测试真实灯具的亮度、色温量化和回报延迟，验证非管理员账号注册表权限降级。
4. 评估 Quest 字体 atlas 内存、面板距离/字号、500+ 实体刷新与操作延迟，再决定后续性能优化。
5. 后续扩展：RGB 颜色、空调等设备控制、OAuth/安全凭证存储、完整中文界面与输入、场景与自动化、空间锚点、语音。当前均未实现，不属于已验收能力。

9 月 22 日的验证没有连接真实 Home Assistant、操作真实家电或构建 APK；10 月 20 日的离线复跑同样不包含这些验收。

## 协议依据

连接、事件订阅和 `call_service` 按 [Home Assistant WebSocket API](https://developers.home-assistant.io/docs/api/websocket/) 实现。
服务成功响应只代表服务调用完成；界面另等状态回传后才显示确认结果。
灯光能力与 Kelvin 色温字段按 [Home Assistant Light entity](https://developers.home-assistant.io/docs/core/entity/light/) 实现。
