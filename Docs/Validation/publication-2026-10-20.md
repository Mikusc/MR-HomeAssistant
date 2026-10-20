# 2026-10-20 发布前检查

本次在独立 checkout 整合已有 dashboard、设备目录、字体和验证资料，并保留默认分支与跨平台设置的已有提交。

## 本次复跑

- `node Tools/Validation/offline-check.mjs --protocol-only`：55 项断言通过。
  使用生产连接、设备、目录与持久化代码；Unity 生命周期 / JSON 由测试 adapter 替代。
  WebSocket 模拟服务仅监听本机回环地址，没有连接真实 Home Assistant 或操作家电。
- Runtime 和 Editor 校验源码：使用 Unity 6000.4.3f1 附带的 Roslyn 编译器以及本机已有 Unity 程序集编译通过。
  这次没有启动 Editor、重新导入完整项目或执行新的 Play Mode 检查。
- 校验脚本语法、Android manifest XML、依赖 JSON、Markdown 本地链接及 `git diff --check` 通过。
- Noto Sans CJK SC 字体校验和与已记录来源一致，附带 SIL Open Font License。
- 对拟提交文本检查凭证格式与私有服务地址，未发现真实凭证或个人 Home Assistant 端点。演示图使用样例设备。

## 保留的历史记录

- `unity-ui-checks.txt`：2026-09-22 的 147 项 Unity UI 断言。
- `unity-playmode-checks.txt`：2026-09-22 的 92 项 Bootstrap Play Mode 断言。
- 本目录 PNG：对应上述 Unity 验证的样例界面截图。

这些历史记录没有被改写为本次复测，也不能作为 Quest 真机或真实家电验收。

## 复现

安装 Node.js 和 Unity 6000.4.3f1 后，在仓库根目录执行：

```sh
node Tools/Validation/offline-check.mjs --protocol-only
```

默认 Unity 路径为 macOS Unity Hub 的安装位置，其他位置可设置 `UNITY_CONTENTS`。
若要使用完整离线编译模式，先用 Unity 打开当前 checkout 并让它生成 `Library/Bee` 引用，再执行不带 `--protocol-only` 的同一命令。

Quest 构建、头显操作、真实服务权限与真实灯具响应仍见[下一步验收](../PROJECT_STATUS.md#下一步验收)。
