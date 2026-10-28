<!-- brand-ignore-file: 本文件为治理说明/模板，内含禁用措辞的教学反例，非生效条款 -->
# FantasyTown-Auth · 许可、品牌与协作治理手册

> **这份文件是“规则说明书”，不是许可证本身。**
> 人和 Agent 在动代码、动文案、动发布之前，先读这一份；真正有法律效力的文本是
> `LICENSE` / `NOTICE.md` / `TRADEMARK.md` / `COMMERCIAL.md`，本手册只讲**怎么维护它们**。

适用仓库：`FantasyTownDev/fantasytown-auth`
当前生效版本：见本仓库 `VERSION` 文件与 git tag。

---

## 0. 一句话原则（背下来再去干活）

> **AGPLv3 管“改了代码交不交源码”，不管“谁拿它赚不赚钱”。**
> 本项目因此 **不禁止商用、不按用户数收费、不要求事前书面授权**。
> 能拦住别人的只有三件事：**① 改代码不交源码（AGPLv3 §13）② 冒充官方／不标衍生（§7(c)）③ 盗用本项目名称标识（商标 + 反法六条）**。

任何“未经书面授权不得盈利 / 会员 / SaaS / 超过 50 用户”的写法都是**自伤条款**（违反 AGPLv3 §10，可被下游删除，国内已有罗盒案类案口径），**本仓库永久禁止出现**。

---

## 1. 文件地图：谁放哪、谁能动

| 文件 | 性质 | 能不能改 | 谁来改 | 说明 |
|---|---|---|---|---|
| `LICENSE` | **纯 AGPL-3.0-only 全文**，一字不改 | 仅升级许可证版本时 | 项目负责人 + 法务 | 不许塞任何项目自有条款 |
| `NOTICE.md` | AGPLv3 **§7 附加条款**（只挂本项目新增材料） | 可微调措辞，不可扩大成禁商 | 项目负责人 | 源文件头部用 `SPDX-NoticeRef` 指向 |
| `TRADEMARK.md` | 商标／命名／品牌政策（**合同外文件**） | 可调整许可范围 | 项目负责人 | 未注册也写“保留权利”，**不标 ®** |
| `COMMERCIAL.md` | 商业授权**独立合同要约**，不是许可证条款 | 可改报价/范围/主体 | 项目负责人（双方法签） | 写“另行书面签订”，**不写“否则侵权”** |
| `COPYRIGHT` | 版权人清单 + 首次发表年份 + 变更史 | 只追加，不删旧行 | 任何人（PR） | Agent 合 PR 时必须同步更新 |
| `THIRD_PARTY_NOTICES` | 依赖许可证归档 | 随依赖增减 | 自动化脚本 + 人复核 | 见 §6 |
| `AI_DECLARATION.md` | AI 辅助生成说明与权属立场 | 几乎不动 | 项目负责人 | 诚实声明，不作权利主张 |
| `VERSION` | `major.minor.patch` + commit + 日期 | 发版时 | 发布脚本 | 对齐 git tag |
| 源文件头部注释 | `Copyright` + `SPDX-License-Identifier` + `SPDX-NoticeRef` | 新增文件必须带 | 作者 / Agent | 模板见 `templates/file-header.cs.txt` |
| **界面 `/auth/about`** | 法律通告 + 源码入口（AGPLv3 §5(d)） | **只能加，不能删** | 后端 | 本项目自己部署也**必须保留** |

> **黄金律**：`LICENSE` 越干净，将来维权越硬。`NOTICE.md` 是刀，`TRADEMARK.md` 是墙，`COMMERCIAL.md` 是生意，三件事分开讲。

---

## 2. 三分钟上手（给 Agent / 新贡献者）

### 2.1 我改了一个文件，要做什么？

```text
□ 源文件头部注释是否齐全（Copyright 年份、SPDX 两行）？
□ 我改的是“新增材料”还是“既有逻辑”？→ 决定是否进 NOTICE.md 的 §7 范围
□ 是否动了排序 / 推荐 / 权重 / 令牌签发 / 校验流程？
   ├─ 是 → 这就是将来 AGPLv3 §13 的“修改版”证据位，务必留清白 commit message
   └─ 否 → 正常流程
□ 是否引入了新依赖？→ 跑 scripts/gen-third-party.sh 重新生成 THIRD_PARTY_NOTICES
□ 是否改了对外文案 / 品牌词？→ 过 §4 的品牌词表
□ 是否动了 LICENSE / NOTICE / TRADEMARK / COMMERCIAL 任一？→ 必须提 PR，不许直推 main
```

### 2.2 Commit 规范（Agent 请照抄）

```
<type>(<scope>): <subject>

<body> 里必须出现以下之一，便于事后取证：
- `legal: touches NOTICE.md`        （动了 §7 附加条款）
- `license: dependency <name>`      （新依赖，需归档）
- `brand: <old> → <new>`            （品牌词变更）
- `alg: <module>`                   （算法/校验/权重类改动，留痕用）

Signed-off-by: <name> <email>       （DCO，人人都要）
```

示例：
```
feat(auth): 重构令牌签发链路

alg: token issuance in YggdrasilAuthService
legal: 行为变更，属 NOTICE.md §7(c) 所指“修改版”证据位

Signed-off-by: zhangsan <zhangsan@xxx.com>
```

---

## 3. AGPLv3 在本项目的“实战判定表”

> 给维权时用，**不是给用户设限**。Agent 做差分取证时照这张表填证据。

| 对方行为 | 算不算“修改 Program” | AGPLv3 §13 | 本项目还能用什么打 |
|---|---|---|---|
| 原样部署 + 页面挂赞助/打赏，输出与基准一致 | 否 | 不触发 | 商标 / 反法六条（若挂我名号） |
| 改排序、权重、推荐、令牌/校验分支，或加支付回调 | **是** | 🔥 必须给修改版对应源码 | 最硬，直接 §13 + §8 |
| 只 UPDATE 数据库里某条对象的数值字段 | 否（改的是数据） | 不触发 | 若“规则性、批量、跨时段”倾斜 → 反推其改了代码/脚本 |
| 新增 DB 字段 / 表 / 触发器（如 `sponsor_flag`） | 构建材料层面是 | 🔥 对应源码含迁移脚本 | 要他交 DDL |
| 删掉 `/auth/about` 或版权/源码入口 | 违反 §5(d) | 🔥 直接违规 | 比“他收费”好告 |
| 挂我名号、不标“独立修改版” | — | §7(c) | + 商标 / 反法六条 |

**基准输出如何固定**（取证核心，见 `docs/forensics.md`）：
1. 用 `VERSION` 对应的 tag + commit 跑公开输入集，录屏 + 哈希归档；
2. 远程打对方实例，同输入三组对照：普通 / 只改赞助状态 / 数值更差却排前；
3. 抓 `X-Algorithm-Version`、报错串、路由、字段名、空结果文案、DB 报错；
4. 公证 + 时间戳；接触可能由“本项目公开可获取”直接推定，举证责任倒置。

---

## 4. 品牌与商标边界（Minecraft 专项）

### 4.1 铁律（照 Mojang Usage Guidelines 写，不是我们自创）

- **“Minecraft”只能做次要描述词**，如副标题、括号说明；**不得做主名、显著性识别、Logo、搜索引流词**。
- 仓库/文档/部署页**醒目位置**（顶部，不是页脚）必须有中英文免责：
  > NOT AN OFFICIAL MINECRAFT PRODUCT. NOT APPROVED BY OR ASSOCIATED WITH MOJANG OR MICROSOFT.
  > 非 Minecraft 官方产品，未经 Mojang 或 Microsoft 批准，亦不与其关联。
- 免责旁必须交代 **① 谁负责 ② 运营者是谁 ③ 联系方式**（聊天群/论坛链接**不算**合格联系方式，至少留邮箱或 issue）。
- **不得**提供或鼓励绕过正版验证的能力；**不得**拿 Minecraft 品牌做商业化包装。
- **`Yggdrasil` 是 Mojang 的协议/服务名，本项目不主张任何权利，也不把它写进自己的商标清单。**

### 4.2 命名允许矩阵

| 用法 | 允许？ | 示例 |
|---|---|---|
| 描述性次要词 | ✅ | `兼容 Yggdrasil 协议的独立验证内核（可用于 Minecraft 第三方登录场景）` |
| 主标题里当显著性识别 | ❌ | ~~`Minecraft 第三方验证登录系统最小化内核`~~ |
| 产品名 / 域名 / Logo 含 Minecraft | ❌ | 一律禁 |
| 如实署名“based on fantasytown-auth” | ✅ | 衍生版必须这么写（§7(c)） |
| 用本项目名号做“官方版/赞助版/企业版”宣传 | ❌ 需书面许可 | 走 `TRADEMARK.md` 申请 |

### 4.3 本项目自己的品牌词（未注册，按“保留权利”管理）

`FantasyTown`、`fantasytown-auth`、项目图标/配色组合。
允许：如实描述、署名、指向上游。
需书面许可：作为其产品名、包名、域名、Logo、赞助/背书宣传。

---

## 5. 发版与自检流水线（人和 Agent 都跑同一套）

```bash
# 1. 更新 VERSION（脚本会校验格式）
echo "0.1.0+$(git rev-parse --short HEAD)+$(date -u +%Y-%m-%d)" > VERSION

# 2. 重新生成第三方归档
bash scripts/gen-third-party.sh

# 3. 校验所有源文件头部是否齐全
bash scripts/check-headers.sh

# 4. 校验品牌词红线（Minecraft 主名、® 误标、禁商措辞）
bash scripts/check-brand.sh

# 5. 校验 LICENSE 仍是纯 AGPL-3.0-only（无自制条款混入）
bash scripts/check-license-purity.sh

# 6. 跑测试 + 录基准输出（取证锚点）
dotnet test && bash scripts/record-baseline.sh

# 7. 打 tag，与 VERSION 一致
git tag -s v$(head -1 VERSION | cut -d+ -f1) -m "Release v$(head -1 VERSION)"
```

CI（`.github/workflows/legal-gate.yml`）在 PR 上自动跑 **2 / 3 / 4 / 5** 四步，任何一步失败就拒合。

---

## 6. 第三方依赖归档规则

- 依赖清单来源：`dotnet list package --include-transitive` + 手动补充 NuGet 许可证 URL；
- 输出到 `THIRD_PARTY_NOTICES`，字段：`包名 | 版本 | 许可证 | 版权 | 源码 URL`；
- AGPL/SSPL 等强传染类依赖要**单独红队提示**（本项目管理态度：优先替换，其次在 NOTICE 中明确传递义务）；
- Agent 禁止在未跑 `gen-third-party.sh` 的情况下合入新 `<PackageReference>`。

---

## 7. 贡献者协议（轻量起步）

当前阶段用 **DCO**（`Signed-off-by`），门槛低、Agent 友好。
当本项目开始对外出售商业授权时，**必须升级为 CLA**（外部 PR 合并前签署，否则无权再许可），
脚本 `scripts/setup-cla.sh` 预留了开关，届时切到 CLA Assistant 即可。

---

## 8. 变更这些文件，必须走 PR + 双人复核

`LICENSE` · `NOTICE.md` · `TRADEMARK.md` · `COMMERCIAL.md` · `COPYRIGHT` · `GOVERNANCE.md` · `scripts/check-license-purity.sh`

任何人（含项目负责人、含 Agent）**禁止直推 main**。PR 模板见 `.github/PULL_REQUEST_TEMPLATE/legal.md`。

---

## 9. 出了事，证据怎么收（给维权执行者）

1. 先固定**本方基准**：`VERSION` tag + commit + 基准输出哈希（早就在发版时录好了）；
2. 远程取证对方：三组对照实验录屏、公证、时间戳；
3. 抓权利指纹：报错串、路由、字段名、版本头 `X-Algorithm-Version`、空结果文案；
4. 发**合规通知函**（模板 `templates/cease-notice.md`）：只写“未按 §13 提供修改版对应源码 / 未保留 §5(d) 通告”，**全文不提“收费、赞助、盈利”**；
5. 30 天不补正 → 授权终止（§8），再诉请停止 + 赔偿；
6. 若他挂我名号 → 另走商标 / 反法六条，不与 AGPL 诉因混写。

---

## 10. 本手册自身的版本

- 修改本手册也算“legal 变更”，走 §8 的 PR 流程；
- 手册版本随仓库 `VERSION`，不单独编号；
- 任何 Agent 在执行任务前，先 `git pull` 并确认本手册无未读更新。

---
