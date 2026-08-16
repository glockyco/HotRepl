#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'USAGE'
Usage:
  hotrepl-build-loader --loader bepinex --assemblies DIR --output DIR [--configuration NAME]
  hotrepl-build-loader --loader melonloader --assemblies DIR --melonloader DIR --il2cpp DIR --output DIR [--configuration NAME]

Build a HotRepl host from the pinned source revision. The command reads explicit
loader assemblies and writes a new output directory. It never deploys to a game.
USAGE
}

loader=""
assemblies=""
melonloader=""
il2cpp=""
output=""
configuration="Release"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --loader) loader="${2:?--loader requires a value}"; shift 2 ;;
    --assemblies) assemblies="${2:?--assemblies requires a value}"; shift 2 ;;
    --melonloader) melonloader="${2:?--melonloader requires a value}"; shift 2 ;;
    --il2cpp) il2cpp="${2:?--il2cpp requires a value}"; shift 2 ;;
    --output) output="${2:?--output requires a value}"; shift 2 ;;
    --configuration) configuration="${2:?--configuration requires a value}"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Error: unknown argument: $1" >&2; usage >&2; exit 2 ;;
  esac
done

case "$loader" in
  bepinex|melonloader) ;;
  *) echo "Error: --loader must be 'bepinex' or 'melonloader'." >&2; exit 2 ;;
esac
[[ -n "$output" ]] || { echo "Error: --output is required." >&2; exit 2; }
[[ -d "$(dirname "$output")" ]] || { echo "Error: output parent does not exist: $(dirname "$output")" >&2; exit 2; }
[[ ! -e "$output" ]] || { echo "Error: output already exists: $output" >&2; exit 2; }

source_root="${HOTREPL_SOURCE:?HOTREPL_SOURCE is not set}"
revision="${HOTREPL_REVISION:?HOTREPL_REVISION is not set}"
workspace=$(mktemp -d "${TMPDIR:-/tmp}/hotrepl-build.XXXXXX")
staging=""
cleanup() {
  rm -rf "$workspace"
  if [[ -n "$staging" ]]; then rm -rf "$staging"; fi
}
trap cleanup EXIT

cp -R "$source_root/." "$workspace/"
chmod -R u+w "$workspace"

inputs=()
require_file() {
  local path="$1"
  [[ -f "$path" ]] || { echo "Error: required assembly is missing: $path" >&2; exit 2; }
  inputs+=("$(realpath "$path")")
}

[[ -d "$assemblies" ]] || { echo "Error: --assemblies must name a directory." >&2; exit 2; }
require_file "$assemblies/UnityEngine.dll"
require_file "$assemblies/UnityEngine.CoreModule.dll"
mkdir -p "$workspace/src/HotRepl.BepInEx/lib"
ln -s "${inputs[0]}" "$workspace/src/HotRepl.BepInEx/lib/UnityEngine.dll"
ln -s "${inputs[1]}" "$workspace/src/HotRepl.BepInEx/lib/UnityEngine.CoreModule.dll"

if [[ "$loader" == "bepinex" ]]; then
  project="src/HotRepl.BepInEx/HotRepl.BepInEx.csproj"
  framework="netstandard2.1"
  build_args=()
else
  [[ -d "$melonloader" ]] || { echo "Error: --melonloader must name a directory." >&2; exit 2; }
  [[ -d "$il2cpp" ]] || { echo "Error: --il2cpp must name a directory." >&2; exit 2; }
  require_file "$melonloader/net6/MelonLoader.dll"
  require_file "$melonloader/net6/Il2CppInterop.Runtime.dll"
  require_file "$il2cpp/UnityEngine.CoreModule.dll"
  require_file "$il2cpp/Il2Cppmscorlib.dll"
  project="src/HotRepl.Host.MelonLoader/HotRepl.Host.MelonLoader.csproj"
  framework="net6.0"
  build_args=(
    "-p:MelonLoaderPath=$(realpath "$melonloader")"
    "-p:Il2CppAssembliesPath=$(realpath "$il2cpp")"
  )
fi

output_abs=$(realpath "$(dirname "$output")")/$(basename "$output")
for protected in "$source_root" "$assemblies" "$melonloader" "$il2cpp"; do
  [[ -n "$protected" ]] || continue
  protected=$(realpath "$protected")
  case "$output_abs/" in
    "$protected/"*) echo "Error: output must not be inside an input: $protected" >&2; exit 2 ;;
  esac
done

(
  cd "$workspace"
  dotnet tool restore
  dotnet restore "$project" --locked-mode --nologo
  dotnet build "$project" --no-restore --nologo -v q -c "$configuration" "${build_args[@]}"
)

parent=$(dirname "$output_abs")
name=$(basename "$output_abs")
staging=$(mktemp -d "$parent/.${name}.tmp.XXXXXX")
cp -R "$workspace/$(dirname "$project")/bin/$configuration/$framework/." "$staging/"
manifest_args=()
for input in "${inputs[@]}"; do
  manifest_args+=(--input "$input")
done
python3 "$source_root/scripts/write-build-manifest.py" \
  --output "$staging" \
  --loader "$loader" \
  --configuration "$configuration" \
  --revision "$revision" \
  "${manifest_args[@]}"
mv "$staging" "$output_abs"
staging=""
printf 'Built %s loader at %s\n' "$loader" "$output_abs"
