#!/usr/bin/env bash
# check-brand.sh —— 品牌词红线校验
#
# 拦截四类问题：
#   1) 误标 ®（本项目/相关标识未注册）
#   2) 自伤型禁商措辞（AGPLv3 §10 下可被删除的无效限制，禁止入库）
#   3) Minecraft 当主标题/显著性识别（应只作次要描述词）
#   4) 醒目位置缺中英文 Minecraft 免责，或缺责任方联系方式
#
# 豁免：文件前 5 行含 'brand-ignore-file' 的整文件跳过（治理文档/模板里的教学反例）。
# 退出码：0 全部通过；1 命中阻断项；纯警告不计入退出码。
set -uo pipefail

ROOT="${1:-.}"
cd "$ROOT"

# 收集要扫的文本文件
mapfile -t SRC < <(find . -type f \( -name '*.md' -o -name '*.txt' -o -name 'COPYRIGHT' -o -name '*.cs' -o -name '*.json' \) \
  -not -path './obj/*' -not -path './bin/*' -not -path './**/node_modules/*' \
  -not -path './.git/*' -not -path './scripts/_add_exempt.py' 2>/dev/null)

if [[ "${#SRC[@]}" -eq 0 ]]; then
  echo "✅ check-brand: 无待检文本（跳过）"; exit 0
fi

# --- 文件级豁免：治理说明/模板整体跳过，只检查真正的入库产物 ---
KEEP=()
for s in "${SRC[@]}"; do
  [[ -f "$s" ]] || continue
  if head -n 5 "$s" 2>/dev/null | grep -qiE 'brand-ignore-file'; then
    continue
  fi
  KEEP+=("$s")
done
TXT=("${KEEP[@]}")
if [[ "${#TXT[@]}" -eq 0 ]]; then
  echo "✅ check-brand: 品牌红线全部通过（待检文件均为豁免的治理/模板文件）"; exit 0
fi

# 生成“去教学反例”的临时副本（.clean 后缀隔离，避开 macOS ._ 元数据）
CLEAN_DIR="$(mktemp -d)" || exit 1
trap 'rm -rf "$CLEAN_DIR"' EXIT
for s in "${TXT[@]}"; do
  base="$(echo "$s" | tr '/' '_' | tr -c '[:alnum:]._-' '_')"
  sed -E \
    -e '/^```/,/^```/d' \
    -e '/^[[:space:]]*[-*][[:space:]]*[✅❌].*((禁止|不得|非商业|用户数|书面)|Minecraft.*主)/d' \
    "$s" > "$CLEAN_DIR/${base}.clean"
done
mapfile -t SCAN < <(find "$CLEAN_DIR" -maxdepth 1 -type f -name '*.clean' 2>/dev/null)
if [[ "${#SCAN[@]}" -eq 0 ]]; then
  echo "✅ check-brand: 无待检文本（跳过）"; exit 0
fi

fail=0
warn=0

# --- 规则 A：® 误标（阻断）---
if grep -rhlE '®' "${SCAN[@]}" >/dev/null 2>&1; then
  echo "❌ 品牌红线：发现 ® 标注。本项目及 FantasyTown 标识尚未注册，不得标注 ®（会构成虚假标注）。"
  fail=1
fi

# --- 规则 B：自伤型禁商措辞（阻断）---
BADWORDS=(
  '非商业使用'
  '禁止商用'
  '不得商用'
  '禁止商业使用'
  '未经.*书面授权'
  '未经.*书面许可'
  '须签署商业许可'
  '必须事先申请'
  '用户数.*50'
  '超过.*用户.*禁止'
  'SaaS.*需授权'
  '收费.*须授权'
)
for w in "${BADWORDS[@]}"; do
  if grep -rhE "$w" "${SCAN[@]}" >/dev/null 2>&1; then
    echo "❌ 自伤条款措辞命中：【$w】"
    echo "    这类写法违反 AGPLv3 §10，可被下游删除（国内已有类案口径），请从入库文本中移除；"
    echo "    需要限制的是『不标衍生身份/盗用名号』，请改走 NOTICE.md §7(c/d/e) + TRADEMARK.md。"
    fail=1
  fi
done

# --- 规则 C：Minecraft 主标题式显著性出现（阻断）---
if grep -rhE '^#{1,3}[[:space:]]+[^#]*Minecraft' "${SCAN[@]}" \
   | grep -qvE '（可用于|兼容|基于|独立|非官方|描述|说明|第三方登录场景'; then
  echo "❌ Minecraft 品牌红线：标题中把 Minecraft 当显著性识别。"
  echo "    改法示例：『fantasytown-auth：兼容 Yggdrasil 协议的独立验证内核（可用于 Minecraft 第三方登录场景）』"
  fail=1
fi

# --- 规则 D：醒目位置须有中英文 Minecraft 免责（阻断）---
hit=0
for f in README.md docs/README.md index.md; do
  [[ -f "$f" ]] || continue
  hit=$((hit+1))
  if ! grep -qiE 'NOT AN OFFICIAL MINECRAFT PRODUCT' "$f"; then
    echo "❌ Minecraft 免责缺失：英文『NOT AN OFFICIAL MINECRAFT PRODUCT』未在 $f 醒目位置出现。"
    fail=1
  fi
  if ! grep -qiE '非.*官方产品|非官方' "$f"; then
    echo "❌ Minecraft 免责免责缺失：中文『非 Minecraft 官方产品…』未在 $f 醒目位置出现。"
    fail=1
  fi
done
[[ "$hit" -eq 0 ]] && echo "ℹ️  未找到 README 等首页文件，跳过 Minecraft 免责位置检查（发布前必须补）。"

# --- 规则 E：免责旁应有责任方联系方式（软提醒，不计入退出码）---
if [[ -f README.md ]]; then
  if ! grep -qiE 'security@|contact@|issue' README.md; then
    echo "⚠️  品牌提示：免责声明旁建议给出责任方邮箱或 issue 渠道（Mojang 口径：聊天群/论坛不算合格联系方式）。"
    warn=1
  fi
else
  echo "⚠️  品牌提示：发布前请在首页补责任方联系方式（邮箱或 issue）。"; warn=1
fi

if [[ "$fail" -eq 0 ]]; then
  echo "✅ check-brand: 品牌红线全部通过（${#TXT[@]} 个文件，${warn} 条软提醒）"
fi
exit "$fail"
