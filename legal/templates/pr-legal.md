<!-- brand-ignore-file: 本文件为治理说明/模板，内含禁用措辞的教学反例，非生效条款 -->
# 变更 PR 模板（legal / brand / license 类变更必填）

> 涉及 `LICENSE` · `NOTICE.md` · `TRADEMARK.md` · `COMMERCIAL.md` · `COPYRIGHT` ·
> `GOVERNANCE.md` · `scripts/check-license-purity.sh` 的 PR **必须走双人复核，禁止直推 main**。

## 变更性质（勾选）

- [ ] 仅文档/措辞微调（不改权利范围）
- [ ] 调整 §7 附加条款范围（需说明依据 §7 哪一款）
- [ ] 商标 / 命名政策调整
- [ ] 商业授权条款调整（注意：不得写成“强制前置许可”）
- [ ] 版权年份 / 贡献者清单追加
- [ ] 第三方依赖新增（附 `scripts/gen-third-party.sh` 输出）

## 自检（作者 + Agent 都跑）

```bash
bash scripts/check-headers.sh
bash scripts/check-brand.sh
bash scripts/check-license-purity.sh
bash scripts/gen-third-party.sh   # 仅依赖变更时
```

- [ ] 以上全部通过（CI 绿）
- [ ] 未引入“非商业/禁止商用/书面授权/用户数限制”等自伤措辞
- [ ] Minecraft 仍为非显著性次要描述，中英文免责保留
- [ ] `VERSION` 与 git tag 已对齐（发版 PR）

## 说明（为什么这么改 / 影响面 / 是否影响既有用户）

<写这里>

## 关联

- 关联 issue / 讨论：<链接>
- 是否需要更新 `templates/cease-notice.md` 或 `forensics.md`：<是/否>
