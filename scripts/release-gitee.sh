#!/usr/bin/env bash
# Gitee 发行版发布脚本（幂等：发行版已存在则复用，附件同名跳过）
#
# 用法:
#   ./scripts/release-gitee.sh <tag> <标题> <notes.md> [附件文件...]
# 示例:
#   ./scripts/release-gitee.sh v1.4.1-preview "v1.4.1-preview 实验版本" \
#       dist/release-notes-v1.4.1-preview.md \
#       dist/HaloPixelLyric-v1.4.1-preview-win-x64.zip
#
# 前置: 环境变量 GITEE_TOKEN（gitee.com/profile/personal_access_tokens 生成，勾选 projects 权限）
#       Windows 设置: 系统属性→环境变量，或 PowerShell:
#         [Environment]::SetEnvironmentVariable('GITEE_TOKEN','<你的令牌>','User')
set -euo pipefail

TAG="$1"; TITLE="$2"; NOTES="$3"; shift 3 || true
: "${GITEE_TOKEN:?未设置 GITEE_TOKEN 环境变量（见脚本头部说明）}"

OWNER_REPO="xia-qiuyu/HaloPixelLyric"
API="https://gitee.com/api/v5/repos/$OWNER_REPO/releases"

json_escape() { python -X utf8 -c 'import json,sys;print(json.dumps(sys.stdin.read(),ensure_ascii=False))' < "$1"; }

# 1) 查已有发行版（幂等）
REL_ID=$(curl -sf "$API/tags/$TAG?access_token=$GITEE_TOKEN" | python -c 'import json,sys;print(json.load(sys.stdin)["id"])' 2>/dev/null || true)

if [ -z "$REL_ID" ]; then
    echo "[1/3] 创建发行版 $TAG ..."
    BODY_JSON=$(json_escape "$NOTES")
    RESP=$(curl -sf -X POST "$API" \
        -d "access_token=$GITEE_TOKEN" \
        -d "tag_name=$TAG" \
        -d "target_commitish=master" \
        --data-urlencode "name=$TITLE" \
        --data-urlencode "body=$BODY_JSON" \
        -d "prerelease=true")
    REL_ID=$(printf '%s' "$RESP" | python -c 'import json,sys;print(json.load(sys.stdin)["id"])')
else
    echo "[1/3] 发行版 $TAG 已存在 (id=$REL_ID)，复用"
fi

echo "[2/3] 发行版 id=$REL_ID"
echo "[3/3] 上传附件 ..."
for f in "$@"; do
    [ -f "$f" ] || { echo "  跳过（不存在）: $f"; continue; }
    BASE=$(basename "$f")
    # 检查同名附件是否已传过
    HAVE=$(curl -sf "$API/$REL_ID?access_token=$GITEE_TOKEN" \
        | python -c 'import json,sys;names=[a.get("name","") for a in json.load(sys.stdin).get("attach_files",[])];print(sys.argv[1] in names)' "$BASE" 2>/dev/null || echo False)
    if [ "$HAVE" = "True" ]; then echo "  已存在，跳过: $BASE"; continue; fi
    echo "  上传: $BASE ($(du -h "$f" | cut -f1)) ..."
    # 注意: Gitee 官方文档写 files[]，实测（2026-11）仅接受单数字段 file
    curl -sf -X POST "$API/$REL_ID/attach_files" \
        -F "access_token=$GITEE_TOKEN" \
        -F "file=@$f" >/dev/null
    echo "  完成: $BASE"
done

echo "发布完成: https://gitee.com/$OWNER_REPO/releases/tag/$TAG"
