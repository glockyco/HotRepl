{
  description = "Pinned HotRepl development and downstream loader build environment";

  inputs = {
    nixpkgs.url = "https://flakehub.com/f/NixOS/nixpkgs/0.2605";

    # Defines the OpenSpec artifact check every repository on this workstation
    # runs, so the commands and the pinned CLI live in one place.
    fleet = {
      url = "github:glockyco/omp-agent-setup";
      inputs.nixpkgs.follows = "nixpkgs";
    };
  };

  outputs =
    {
      self,
      nixpkgs,
      fleet,
    }:
    let
      systems = [
        "aarch64-darwin"
        "x86_64-darwin"
        "aarch64-linux"
        "x86_64-linux"
      ];
      forAllSystems = nixpkgs.lib.genAttrs systems;
      revision = self.rev or self.dirtyRev or "dirty-local-checkout";

      dotnetPackages =
        pkgs:
        if pkgs.stdenv.hostPlatform.isDarwin then
          pkgs.dotnetCorePackages.overrideScope (
            _final: previous: {
              runtime_10_0 = previous."runtime_10_0-bin";
              sdk_10_0 = previous."sdk_10_0-bin";
            }
          )
        else
          pkgs.dotnetCorePackages;

      toolchain =
        pkgs:
        let
          dotnet = (dotnetPackages pkgs).sdk_10_0;
          bun = pkgs.callPackage ./nix/bun-bin.nix { };
        in
        {
          inherit bun dotnet;
          packages = [
            bun
            dotnet
            pkgs.actionlint
            pkgs.commitlint
            pkgs.coreutils
            pkgs.dprint
            pkgs.git
            pkgs.lefthook
            pkgs.nodejs_24
            pkgs.python3
            pkgs.typos
          ];
        };
    in
    {
      packages = forAllSystems (
        system:
        let
          pkgs = nixpkgs.legacyPackages.${system};
          tools = toolchain pkgs;
        in
        {
          default = tools.bun;
          build-loader = pkgs.writeShellApplication {
            name = "hotrepl-build-loader";
            runtimeInputs = tools.packages;
            text = ''
              export HOTREPL_SOURCE=${self}
              export HOTREPL_REVISION=${nixpkgs.lib.escapeShellArg revision}
              exec ${./scripts/build-loader.sh} "$@"
            '';
          };
          doctor = pkgs.writeShellApplication {
            name = "hotrepl-doctor";
            runtimeInputs = tools.packages;
            text = ''
              printf 'bun %s\n' "$(bun --version)"
              printf 'dotnet %s\n' "$(dotnet --version)"
              dprint --version
              printf 'lefthook %s\n' "$(lefthook version)"
              printf 'commitlint %s\n' "$(commitlint --version)"
              printf 'typos %s\n' "$(typos --version)"
              printf 'actionlint %s\n' "$(actionlint --version)"
              printf 'revision %s\n' ${nixpkgs.lib.escapeShellArg revision}

              if [[ -d lib && -d src/HotRepl.BepInEx/lib && -f Local.props ]]; then
                echo 'optional BepInEx host inputs: available'
              else
                echo 'optional BepInEx host inputs: unavailable'
                echo '  Host builds require local Unity assemblies in lib/ and src/HotRepl.BepInEx/lib plus Local.props.'
                echo '  Core, protocol, SDK, CLI, MCP, and test work remain available.'
              fi
            '';
          };
          check = pkgs.writeShellApplication {
            name = "hotrepl-check";
            runtimeInputs = tools.packages;
            text = ''
              if [[ ! -f flake.nix || ! -f lefthook.yml ]]; then
                echo "Run nix run .#check from the HotRepl repository root." >&2
                exit 1
              fi
              export HOTREPL_DEV_SHELL=1
              exec lefthook run pre-push --force
            '';
          };
        }
      );

      apps = forAllSystems (
        system:
        let
          packages = self.packages.${system};
        in
        {
          default = {
            type = "app";
            program = "${packages.check}/bin/hotrepl-check";
          };
          build-loader = {
            type = "app";
            program = "${packages.build-loader}/bin/hotrepl-build-loader";
          };
          check = {
            type = "app";
            program = "${packages.check}/bin/hotrepl-check";
          };
          doctor = {
            type = "app";
            program = "${packages.doctor}/bin/hotrepl-doctor";
          };
        }
      );

      devShells = forAllSystems (
        system:
        let
          pkgs = nixpkgs.legacyPackages.${system};
          tools = toolchain pkgs;
        in
        {
          default = pkgs.mkShell {
            name = "hotrepl-dev";
            packages = tools.packages;
            shellHook = ''
              export HOTREPL_DEV_SHELL=1
              export DOTNET_CLI_TELEMETRY_OPTOUT=1
              export DOTNET_NOLOGO=1
              if repo_root=$(git rev-parse --show-toplevel 2>/dev/null); then
                export REPO_ROOT="$repo_root"
              fi
            '';
          };
        }
      );

      checks = forAllSystems (system: {
        openspec = fleet.lib.openspecCheck {
          pkgs = nixpkgs.legacyPackages.${system};
          src = ./.;
        };
        devShell = self.devShells.${system}.default;
        inherit (self.packages.${system})
          build-loader
          check
          doctor
          ;
      });

      formatter = forAllSystems (system: nixpkgs.legacyPackages.${system}.nixfmt-tree);
    };
}
