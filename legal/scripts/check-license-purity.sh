#!/usr/bin/env bash
# check-license-purity.sh —— 保证 LICENSE 是“纯 AGPL-3.0-only”，不被自制条款污染
# 判定依据：
#   1) 文件首部/内容含 SPDX AGPL-3.0-only；
#   2) 不含“商业/授权/50 用户/书面”等项目自制限制；
#   3) 长度接近 FSF 原文（> 300 行），避免只放占位符。
set -euo pipefail
ROOT="${1:-.}"
cd "$ROOT"
LIC="$ROOT/LICENSE"

[[ -f "$LIC" ]] || { echo "ℹ️  当前目录无 LICENSE，跳过纯净度检查（发布前必须有）"; exit 0; }

fail=0
if ! grep -qE 'SPDX-License-Identifier: *AGPL-3\.0-only' "$LIC"; then
  echo "❌ LICENSE 缺少 'SPDX-License-Identifier: AGPL-3.0-only'"
  fail=1
fi

if grep -qE '非商业|禁止商用|不得商用|商业使用|50 ?用户|书面授权|书面许可|须签署|事先申请' "$LIC"; then
  echo "❌ LICENSE 被自制限制污染（禁商/用户数/书面授权）——请恢复为 FSF 原文，项目条款放 NOTICE.md"
  fail=1
fi

lines=$(wc -l < "$LIC")
if [[ "$lines" -lt 300 ]]; then
  echo "⚠️  LICENSE 仅 $lines 行，疑似占位符而非 FSF AGPL-3.0-only 全文，请替换为官方原文"
  fail=1
fi

if [[ "$fail" -eq 0 ]]; then
  echo "✅ check-license-purity: LICENSE 为纯净 AGPL-3.0-only（$lines 行）"
fi
exit "$fail"
