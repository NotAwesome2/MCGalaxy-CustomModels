{
  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-25.05";
  };

  # build with `.?submodules=1`
  outputs = { self, nixpkgs }:
    let
      inherit (nixpkgs) lib;

      makePackages = (pkgs: {
        default = pkgs.stdenv.mkDerivation {
          pname = "CustomModels";
          version = "0.0.1"; # must be only 3 numbers or else dotnet freaks

          src = lib.sourceByRegex ./. [
            "^CustomModels(/.*)?$"
            "^CustomModels\.sln$"
            "^MCGalaxy(/.*)?$"
            "^Newtonsoft\.Json\.dll$"
          ];

          dontConfigureNuget = true;

          nativeBuildInputs = with pkgs; [
            dotnet-sdk_9
            mono
          ];

          FrameworkPathOverride = "${pkgs.mono}/lib/mono/4.7.2-api";

          buildPhase = ''
            dotnet restore
            dotnet build --no-restore --configuration Release
            dotnet test --no-restore --verbosity normal
          '';

          installPhase = ''
            install -Dm 644 \
              ./CustomModels/bin/Release/CustomModels.dll \
              $out/lib/CustomModels.dll
          '';
        };
      });
    in
    builtins.foldl' lib.recursiveUpdate { } (builtins.map
      (system:
        let
          pkgs = import nixpkgs {
            inherit system;
          };
          packages = makePackages pkgs;
        in
        {
          devShells.${system} = packages;
          packages.${system} = packages;
        })
      lib.systems.flakeExposed);
}
