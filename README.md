# SPAM 简体中文补丁 0.2.0

支持当前 Windows x64 / Unity Mono 版 SPAM，初版目标版本 Steam build 25941248。插件源代码和译文文件可独立维护。

随包包含 528 条经原文校对的译文和 3008 条草稿记录，其中 339 条完整效果说明采用固定中文句式。规则正文大部分仍待人工验收；完整修复与验证范围见发行包 `验证记录.md`。

## 使用

关闭游戏，解压发行包 `SPAM-zhCN-0.2.0.zip`，双击 `install.cmd`。可直接覆盖升级 0.1。默认游戏目录为 `E:\SteamLibrary\steamapps\common\SPAM`；其他路径在终端运行 `install.cmd -GameDir "D:\游戏库\SPAM"`。安装器先检查文件来源、校验值和已有文件，保留游戏原始程序和资源；仅清理本补丁拥有且未被修改的旧字体。

从 Steam 启动游戏，语言选择 **English**。默认显示中文；**按住 F8** 查看当前英文原文，松开恢复中文。标题用站酷快乐体，正文用真实 ExtraBold 800 粗黑体，缺字有后备字体。已校对与草稿共用字体，草稿文字加下划线，图标不加线。

未知或已变更的原文在后台初翻，等待/失败期间显示英文，成功且标记检查通过后更新当前界面。初翻使用免密钥接口，可能限流或失效；结果仍是草稿，不能当已验收规则。相同原文缓存后不再重复请求。不会翻译聊天输入或主动修改牌组名称。

插件设置在游戏目录 `BepInEx/config/com.spam.zhcn.cfg`：`Enabled` 为总开关，`OriginalKey` 可改原文按键，`AutoTranslate` 控制后台初翻。共享补丁不携带账号或 API 密钥。

`[Appearance]` 的 `DraftStyle` 可选 `Underline`（默认下划线）、`Italic`（斜体）、`None`（取消标记）；修改后重启。取消显示标记不会改变译文的验收状态。

人工验收参照随包 `手动验收流程-0.2.md`，在 `手动验收记录-0.2.csv` 填写修订与结论后集中反馈即可。旧版记录表保留，不覆盖已有批注。

## 译文维护

插件目录 `BepInEx/plugins/SpamChinese` 的 `verified.json` 是校对译文；`drafts.seed.json` 是随包预制的机译草稿；`drafts.cache.json` 是本机新初翻缓存。每个条目记录 key、英文原文、原文 SHA-256、译文和状态。英文内容改变时，旧译文失效；机译不能自行升级为已验收。

保存占位符、图标、颜色、链接标签。机制说明优先按“对象→效果与数值→次数/条件”组织，数字与元素作为整体保护。范围不解释为目标数量，额外伤害不混同元素加成，冷却不擅自补充为回合。复杂句不作半句规则改写。合法的多段效果图标保留；只阻止重复加工和错误引用产生的多余图标。

维护项目中的 `curated.json` 按 key 保存的修订另有 `keySourceHashes` 指纹约束；更新游戏原文后不能直接沿用旧条目的验收状态。只刷新校对包可用 `python scripts/translate_seed.py --curated-only`，不会请求在线初翻。

## 卸载

关闭游戏后双击发行包中的 `uninstall.cmd`；自定义安装路径需同样指定 `-GameDir`。只删除安装清单中属于补丁且未被其他工具改动的文件；其他插件、修改过的文件、本机缓存与配置保留。卸载不需要重装游戏。

## 构建

需要 .NET SDK、游戏本地程序集以及校验过的 BepInEx 5.4.23.5。用 `scripts/prepare_data.py` 导出原文，`py -3.13 scripts/prepare_fonts.py` 准备静态便携字体。`scripts/translate_seed.py` 管理译文，`dotnet run --project tests/CoreTests.csproj -- --rebuild-mechanics data` 离线生成规则句式，随后 `scripts/package.ps1`。字体转换使用 fontTools 4.67.0，数据源与哈希随字体发布。

BepInEx 5.4.23.5 来源：https://github.com/BepInEx/BepInEx （MIT）；加载器 UnityDoorstop 使用 LGPL-2.1，其他依赖许可见发行包 `licenses`。字体来自 Google Fonts Noto Sans SC / ZCOOL KuaiLe（OFL，许可与来源随字体发布）。补丁没有分发游戏程序集或原始图片。

联机兼容和账号政策无法仅由文字显示正常证明；官方修改条款和用户已接受的本地试验范围见 `research/SPAM-官方资料调研.md`。此初版不主动替用户进入匹配、购买或提交外部反馈。
