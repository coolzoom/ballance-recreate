#!/usr/bin/env bash
# Ballance Revival：配置、编译、启动。
# 用法:
#   ./start.sh        打开菜单，输入 1、2、3… 选择
#   ./start.sh 1      直接执行对应序号后退出

set -u

ROOT="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$ROOT/.." && pwd)"
PROJECT="$ROOT/BallanceRevival.csproj"
ASSETS="$REPO/Ballance.Build.12799282"
SDK_MAJOR="10"

pause_menu() {
  if [[ -t 0 && "${DIRECT:-0}" -eq 0 ]]; then
    echo
    read -r -p "按回车返回菜单..." _
  fi
}

need_dotnet() {
  if ! command -v dotnet >/dev/null 2>&1; then
    echo "未找到 dotnet。请安装 .NET ${SDK_MAJOR} SDK："
    echo "  https://dotnet.microsoft.com/download/dotnet/${SDK_MAJOR}.0"
    return 1
  fi
  if ! dotnet --list-sdks 2>/dev/null | grep -q "^${SDK_MAJOR}\."; then
    echo "当前已安装的 SDK："
    dotnet --list-sdks 2>/dev/null || true
    echo "本项目需要 .NET ${SDK_MAJOR} SDK。"
    echo "  https://dotnet.microsoft.com/download/dotnet/${SDK_MAJOR}.0"
    return 1
  fi
}

configure() {
  echo "== 配置环境 =="
  need_dotnet || return 1
  echo "SDK: $(dotnet --version)"

  if [[ ! -d "$ASSETS/3D Entities" ]]; then
    echo "未找到游戏资源目录："
    echo "  $ASSETS"
    echo "请把 Ballance 资源放在仓库的 Ballance.Build.12799282 下。"
    return 1
  fi
  echo "资源: $ASSETS"

  echo "还原 NuGet 包..."
  dotnet restore "$PROJECT" --nologo
}

build_config() {
  local cfg="$1"
  echo "== 编译 ${cfg} =="
  need_dotnet || return 1
  dotnet build "$PROJECT" -c "$cfg" --nologo
}

run_game() {
  local cfg="$1"
  shift
  local out="$ROOT/bin/${cfg}/net${SDK_MAJOR}.0/BallanceRevival.dll"
  if [[ ! -f "$out" ]]; then
    echo "尚未编译 ${cfg}，先编译再启动。"
    build_config "$cfg" || return 1
  fi
  echo "== 启动游戏 (${cfg}) =="
  echo "工作目录: $ROOT"
  cd "$ROOT"
  dotnet run --project "$PROJECT" -c "$cfg" --no-build -- "$@"
}

verify_levels() {
  echo "== 校验全部关卡 =="
  need_dotnet || return 1
  cd "$ROOT"
  dotnet run --project "$PROJECT" -c Debug -- --verify
}

clean_output() {
  echo "== 清理编译输出 =="
  need_dotnet || return 1
  dotnet clean "$PROJECT" --nologo
}

one_click() {
  configure || return 1
  build_config Debug || return 1
  run_game Debug
}

show_menu() {
  cat <<'EOF'

=============================================
  Ballance Revival   配置 / 编译 / 启动
=============================================
  1) 一键：配置 + 编译 + 启动
  2) 配置环境（检查 SDK、确认资源、还原依赖）
  3) 编译 Debug
  4) 编译 Release
  5) 启动游戏（Debug）
  6) 启动游戏（Release）
  7) 校验全部关卡
  8) 清理编译输出
  0) 退出
EOF
}

dispatch() {
  case "$1" in
    1) one_click ;;
    2) configure ;;
    3) build_config Debug ;;
    4) build_config Release ;;
    5) run_game Debug ;;
    6) run_game Release ;;
    7) verify_levels ;;
    8) clean_output ;;
    0) return 0 ;;
    *)
      echo "无效序号: $1"
      return 1
      ;;
  esac
}

if [[ $# -ge 1 ]]; then
  DIRECT=1
  dispatch "$1"
  exit $?
fi

DIRECT=0
while true; do
  show_menu
  read -r -p "请输入序号: " choice
  if [[ "$choice" == "0" ]]; then
    echo "已退出。"
    exit 0
  fi
  dispatch "$choice"
  status=$?
  if [[ $status -ne 0 ]]; then
    echo "执行失败 (退出码 ${status})。"
  fi
  pause_menu
done
