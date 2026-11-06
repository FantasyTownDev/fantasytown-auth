#!/usr/bin/env bash
# check-headers.sh —— 校验所有纳入发布的源文件是否带齐 SPDX + 版权头部
# 用法：bash scripts/check-headers.sh [目录，默认 .]
# 退出码：0 通过，1 发现缺失（CI 会失败）
set -euo pipefail

ROOT="${1:-.}"
cd "$ROOT"

# 只扫代码文件；按项目实际增减后缀
mapfile -t FILES < <(find . \
  -type f \( -name '*.cs' -o -name '*.fs' -o -name '*.ts' -o -name '*.js' -o -name '*.java' -o -name '*.go' \) \
  -not -path './obj/*' -not -path './bin/*' -not -path './**/node_modules/*' \
  -not -path './**/generated/*' -not -path './**/Migrations/*')

bad=0
for f in "${FILES[@]}"; do
  # 允许机器生成文件用 tool-generated 注释豁免，但必须仍带 SPDX
  if ! grep -qE 'SPDX-License-Identifier:' "$f"; then
    echo "MISSING SPDX: $f"; bad=$((bad+1)); continue
  fi
  if ! grep -qE 'Copyright.*FantasyTownDev' "$f"; then
    echo "MISSING COPYRIGHT: $f"; bad=$((bad+1))
  fi
done

if [[ "$bad" -eq 0 ]]; then
  echo "✅ check-headers: 全部源文件头部齐全 (${#FILES[@]} files)"
else
  echo "❌ check-headers: $bad 处缺失，请按 templates/file-header.cs.txt 补齐"
  exit 1
fi
