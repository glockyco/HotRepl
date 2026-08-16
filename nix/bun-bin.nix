{
  lib,
  stdenvNoCC,
  fetchurl,
  unzip,
}:

let
  version = "1.3.14";
  artifacts = {
    aarch64-darwin = {
      archive = "bun-darwin-aarch64.zip";
      directory = "bun-darwin-aarch64";
      hash = "sha256-2LliIYKK1vl6x6wKt+lYcjQa92MAHogD6CZ2UsJlJiA=";
    };
    x86_64-darwin = {
      archive = "bun-darwin-x64.zip";
      directory = "bun-darwin-x64";
      hash = "sha256-QYPfM3RiPlurMVxUfPoJdFM81FfYa3O2OfeoeXTNZjM=";
    };
    aarch64-linux = {
      archive = "bun-linux-aarch64.zip";
      directory = "bun-linux-aarch64";
      hash = "sha256-on/7Y6gxA3WDbg1vZorhf6jY0YuIw3yCHGUzGXOhmjs=";
    };
    x86_64-linux = {
      archive = "bun-linux-x64.zip";
      directory = "bun-linux-x64";
      hash = "sha256-lR7iruhV8IWVruxiJSJqKY0/6oOj3NZGXAnLzN9+hI8=";
    };
  };
  artifact = artifacts.${stdenvNoCC.hostPlatform.system};
in
stdenvNoCC.mkDerivation {
  pname = "bun-bin";
  inherit version;

  src = fetchurl {
    name = artifact.archive;
    url = "https://github.com/oven-sh/bun/releases/download/bun-v${version}/${artifact.archive}";
    inherit (artifact) hash;
  };

  sourceRoot = ".";
  nativeBuildInputs = [ unzip ];

  installPhase = ''
    runHook preInstall
    install -Dm755 "${artifact.directory}/bun" "$out/bin/bun"
    runHook postInstall
  '';

  meta = {
    description = "Fast JavaScript runtime, bundler, test runner, and package manager";
    homepage = "https://bun.sh";
    license = lib.licenses.mit;
    mainProgram = "bun";
    platforms = builtins.attrNames artifacts;
    sourceProvenance = [ lib.sourceTypes.binaryNativeCode ];
  };
}
