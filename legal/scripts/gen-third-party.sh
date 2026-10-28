#!/usr/bin/env bash
# gen-third-party.sh —— 生成 THIRD_PARTY_NOTICES
# 优先用 dotnet，缺失则给手动模板。输出到仓库根 THIRD_PARTY_NOTICES
set -euo pipefail
ROOT="${1:-.}"
cd "$ROOT"
OUT="$ROOT/THIRD_PARTY_NOTICES"

{
  echo "# 第三方依赖与许可证归档"
  echo
  echo "> 自动生成于 $(date -u +%Y-%m-%dT%H:%M:%SZ) · 由 scripts/gen-third-party.sh 生成"
  echo "> 字段：包名 | 版本 | 许可证 | 版权 | 源码/项目 URL"
  echo "> 注意：AGPL/SSPL 等强传染类依赖请在下方红队区单独确认传递义务。"
  echo
  echo "| 包名 | 版本 | 许可证 | 版权 | 源码 |"
  echo "| --- | --- | --- | --- | --- |"
} > "$OUT.tmp"

if command -v dotnet >/dev/null 2>&1; then
  dotnet list package --include-transitive --format json 2>/dev/null \
    | python3 - <<'PY'
import json,sys
try:
    d=json.load(sys.stdin)
except Exception:
    raise SystemExit("dotnet 输出非 JSON，请手动填写 THIRD_PARTY_NOTICES")
projects=d.get("projects",[])
seen=set()
for p in projects:
    for dep in p.get("frameworks",{}).values():
        for dd in dep.get("dependencies",[]):
            key=(dd.get("name"),dd.get("version"))
            if key in seen: continue
            seen.add(key)
            lic=dd.get("license","?")
            src=dd.get("repositoryUrl") or dd.get("projectUrl") or ""
            print(f"| {dd.get('name')} | {dd.get('version')} | {lic} | — | {src} |")
PY
else
  echo "| （未检测到 dotnet CLI，请手动填写） | — | — | — | — |" >> "$OUT.tmp"
fi

{
  echo
  echo "## 红队区：强传染类依赖确认"
  echo
  echo "- [ ] 本列表中出现 AGPL / SSPL / 类 AGPL 依赖时，须评估：是否将其替换，"
  echo "      或在本项目 NOTICE 中明确其源码传递义务。"
  echo "- [ ] 每个依赖的许可证原文请随发布包一并归档（NuGet 许可证 URL 可脚本抓取）。"
} >> "$OUT.tmp"

mv "$OUT.tmp" "$OUT"
echo "✅ 已生成 $OUT（$(wc -l < "$OUT") 行，请人工复核许可证字段）"
