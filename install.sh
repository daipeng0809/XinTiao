#!/usr/bin/env bash
# ============================================================
#  信息调试助手（XinTiao）Linux 安装脚本
#
#  作用：
#    1) 自动定位程序可执行文件与图标文件的“真实位置”；
#    2) 按 XinTiao.desktop 模板生成桌面启动文件，
#       其中 Exec / Icon / Path 全部填写为当前真实的绝对路径；
#    3) 复制到应用菜单目录（普通用户 ~/.local/share/applications，
#       root 用户 /usr/share/applications），并刷新菜单缓存。
#
#  用法：
#    bash install.sh                              # 自动识别同目录下的程序
#    bash install.sh /opt/xintiao/信息调试助手     # 手动指定程序路径
#    sudo bash install.sh                         # 安装给所有用户
#
#  提示：若从 Windows 复制到 Linux 后提示 $'\r' 之类错误，
#        先执行：sed -i 's/\r$//' install.sh
# ============================================================

set -u

SCRIPT_DIR="$(cd "$(dirname "$(readlink -f "$0" 2>/dev/null || echo "$0")")" && pwd)"

# ---------- 1. 定位程序可执行文件 ----------
APP_EXE="${1:-}"

if [ -z "$APP_EXE" ]; then
    # 1.1 按发布名查找
    for name in "信息调试助手" "XinTiao" "xintiao"; do
        if [ -f "$SCRIPT_DIR/$name" ] && [ -x "$SCRIPT_DIR/$name" ]; then
            APP_EXE="$SCRIPT_DIR/$name"
            break
        fi
    done
fi

if [ -z "$APP_EXE" ]; then
    # 1.2 退一步：找同目录下任意 ELF 可执行文件
    while IFS= read -r file; do
        case "$file" in
            *.sh|*.desktop|*.json|*.txt|*.so|*.dll|*.pdb|*.png|*.ico|*.db|*.log) continue ;;
        esac
        if [ -x "$file" ] && head -c 4 "$file" 2>/dev/null | grep -q "ELF"; then
            APP_EXE="$file"
            break
        fi
    done < <(find "$SCRIPT_DIR" -maxdepth 1 -type f | sort)
fi

if [ -z "$APP_EXE" ] || [ ! -f "$APP_EXE" ]; then
    echo "错误：没有找到程序文件。" >&2
    echo "请把本脚本放到程序所在目录执行，或指定程序路径：" >&2
    echo "    bash $0 /路径/信息调试助手" >&2
    exit 1
fi

# ---------- 2. 规范为绝对路径 ----------
APP_EXE="$(readlink -f "$APP_EXE" 2>/dev/null || echo "$APP_EXE")"
APP_DIR="$(dirname "$APP_EXE")"
APP_NAME="$(basename "$APP_EXE")"

if [ ! -x "$APP_EXE" ]; then
    chmod +x "$APP_EXE" 2>/dev/null \
        || echo "警告：$APP_EXE 没有执行权限，请手动执行 chmod +x \"$APP_EXE\"" >&2
fi

# ---------- 3. 定位图标（优先 png，桌面环境兼容性最好） ----------
ICON_PATH=""
for candidate in \
    "$APP_DIR/Assets/wk.png" \
    "$APP_DIR/Assets/wk-256x256.ico" \
    "$APP_DIR/wk-256x256.ico" \
    "$APP_DIR/Assets/avalonia-logo.ico"
do
    if [ -f "$candidate" ]; then
        ICON_PATH="$candidate"
        break
    fi
done

if [ -n "$ICON_PATH" ]; then
    ICON_LINE="Icon=$ICON_PATH"
else
    ICON_LINE="Icon=application-x-executable"
    echo "提示：没有找到图标文件，桌面文件将使用系统默认图标。" >&2
fi

# ---------- 4. 生成 desktop 文件内容 ----------
DESKTOP_CONTENT="[Desktop Entry]
Encoding=UTF-8
Type=Application
Name=XinTiao
Name[zh_CN]=信息调试助手
Name[CN]=信息调试助手
Comment=Information Debugging Assistant
Comment[zh_CN]=串口 / TCP / UDP 信息调试助手
Exec=\"$APP_EXE\"
Path=$APP_DIR
$ICON_LINE
Terminal=false
Categories=Utility;
StartupNotify=true"

TMP_DESKTOP="$(mktemp /tmp/xintiao-desktop.XXXXXX)"
printf '%s\n' "$DESKTOP_CONTENT" > "$TMP_DESKTOP"
chmod 644 "$TMP_DESKTOP"

# 4.1 在程序目录生成一份（便于随程序一起分发 / 查看）
if cp "$TMP_DESKTOP" "$APP_DIR/XinTiao.desktop" 2>/dev/null; then
    chmod +x "$APP_DIR/XinTiao.desktop" 2>/dev/null
    echo "已生成：$APP_DIR/XinTiao.desktop"
else
    echo "警告：$APP_DIR 不可写，跳过在程序目录生成 desktop 文件。" >&2
fi

# ---------- 5. 安装到应用菜单目录 ----------
if [ "$(id -u)" -eq 0 ]; then
    TARGET_DIR="/usr/share/applications"
else
    TARGET_DIR="$HOME/.local/share/applications"
fi

if mkdir -p "$TARGET_DIR" 2>/dev/null && cp "$TMP_DESKTOP" "$TARGET_DIR/XinTiao.desktop" 2>/dev/null; then
    chmod +x "$TARGET_DIR/XinTiao.desktop" 2>/dev/null
    echo "已安装到应用菜单：$TARGET_DIR/XinTiao.desktop"
else
    echo "警告：无法写入 $TARGET_DIR，可尝试：sudo bash $0 \"$APP_EXE\"" >&2
fi

# ---------- 6. 刷新桌面数据库（有就刷新，没有忽略） ----------
if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database "$TARGET_DIR" >/dev/null 2>&1 || true
fi

rm -f "$TMP_DESKTOP"

# ---------- 7. 输出结果 ----------
echo "------------------------------------------------------------"
echo "程序路径：$APP_EXE"
echo "工作目录：$APP_DIR"
echo "图标路径：${ICON_PATH:-（未找到，使用系统默认图标）}"
echo "程序名称：$APP_NAME"
echo "完成。可在应用菜单中搜索“信息调试助手”或“XinTiao”启动。"
echo "------------------------------------------------------------"
