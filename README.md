# v2rayn-g：Xboard 登录与多订阅桥接层

本目录以 **2026-08-22** 拉取的 `2dust/v2rayN` 与 `2dust/v2rayNG` 源码为宿主，新增一层只负责账号鉴权和订阅目录同步的桥接代码。

## 已固定的上游源码

- `v2rayN/`：`2dust/v2rayN`，当前 HEAD `af0eb9ed14638fa877d11c235e491442ec7ba215`
- `v2rayNG/`：`2dust/v2rayNG`，当前 HEAD `63f557242bdd071214c4037c76c912b66da925c8`

上游项目保留各自的 Git 元数据和许可证；本目录的新增代码不修改上游许可证。更新源码请运行 `scripts/update-upstream.ps1`，脚本会使用 fast-forward 并重新生成版本记录。

## 目标行为

1. 用户在客户端输入 Xboard 面板地址、邮箱、密码。
2. 调用 `POST /api/v1/passport/auth/login`，只在内存中保存 `auth_data` Bearer token。
3. 调用带 Bearer token 的 `GET /api/v1/user/getSubscribe`。
4. 读取 `subscriptions`，兼容后端已有的 `plans` / `profiles` 回退字段。
5. **每个套餐保存为独立订阅项，不合并**；因此内部节点、权限组 2（vip-2 外部订阅）返回的外部地址、以及 BitzNet 等新外部地址都能分别切换和更新。
6. 客户端只导入 Xboard 鉴权接口返回的地址。权限组、流量、到期日仍由 Xboard 服务端决定；客户端的过期/流量判断只是避免导入明显不可用的项，不能替代服务端校验。

外部订阅 URL 会原样保留，不会把内部 `flag` 参数错误地追加到外部提供商 URL。与面板同源的内部 URL 才会追加 `flag=v2rayn-g`，便于 Xboard 识别客户端格式。

## 目录

- `shared/xboard-subscription.schema.json`：跨 Windows/Android 的响应约定。
- `desktop/`：可编译的 .NET 8 桥接 CLI/库，可作为 v2rayN `ServiceLib` 的登录入口或适配层。
- `android/`：可直接复制到 v2rayNG Android module 的 Kotlin API client。
- `scripts/update-upstream.ps1`：更新两个上游仓库并记录 SHA。
- `config.example.json`：不含任何真实 token 的配置示例。

## 桌面桥接运行

```powershell
$env:XBOARD_EMAIL = 'user@example.com'
$env:XBOARD_PASSWORD = '<password>'
dotnet run --project .\desktop -- `
  --panel-url https://panel.example.com `
  --json-out .\desktop\subscriptions.json
```

密码不写命令行，避免进入 PowerShell 历史记录。`subscriptions.json` 包含可导入的 URL，请不要提交到 Git。

只验证响应解析：

```powershell
dotnet run --project .\desktop -- --fixture .\desktop\fixtures\get-subscribe.json
```

## v2rayN / v2rayNG 接入点

- v2rayN：登录成功后，把 `XboardSubscriptionProfile` 映射为原有 `SubItem`，每项一个 `Id`，`Url` 使用 `SubscribeUrl`，然后调用上游已有的 `SubscriptionHandler.UpdateProcess`。
- v2rayNG：登录成功后，把每个 profile 映射为原有 `SubscriptionItem`，保留独立 `remarks` / `subId`，然后调用上游 `SubscriptionUpdateService`。

不要把 Xboard 的 `auth_data`、订阅 token 或完整订阅 URL 写入日志；持久化时应使用系统安全存储，示例 CLI 只用于本地验证和集成开发。

## 重要边界

本次改动完成的是通用鉴权/多套餐桥接层和上游源码固定。若要交付带登录页面、Android VPN 权限引导、Windows 安装包和签名的最终产品，还需要在两个上游 UI 中接入对应按钮/页面并分别执行 Windows 与 Android 的正式构建；不应把这一步误认为已经由 API bridge 自动完成。
## Android integration note

`android/XboardSubscriptionParser.kt` is deliberately independent from the existing v2rayNG storage classes. In the native UI/service layer, call `XboardSubscriptionParser.parse(...)` after the authenticated request, then map each returned profile to one existing `SubscriptionItem` / `SubItem` record. Use a stable key based on `subscriptionId` when present; never merge profiles by panel host because that would combine the internal and external packages.
