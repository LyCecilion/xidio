{
  description = "xidio .NET development environment";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
  };

  outputs =
    { nixpkgs, ... }:
    let
      supportedSystems = [
        "x86_64-linux"
        "aarch64-linux"
        "aarch64-darwin"
      ];

      forAllSystems = nixpkgs.lib.genAttrs supportedSystems;
    in
    {
      devShells = forAllSystems (
        system:
        let
          pkgs = import nixpkgs { inherit system; };
        in
        {
          default = pkgs.mkShell {
            packages = with pkgs; [
              dotnet-sdk_10
              git
              pkg-config
            ]
            ++ lib.optionals stdenv.isLinux [
              clang
              iproute2
              iw
              lld
              networkmanager
            ];

            buildInputs = with pkgs; [
              icu
              openssl
              zlib
            ];

            DOTNET_CLI_TELEMETRY_OPTOUT = "1";
            DOTNET_NOLOGO = "1";

            LD_LIBRARY_PATH = pkgs.lib.optionalString pkgs.stdenv.isLinux (
              pkgs.lib.makeLibraryPath [
                pkgs.icu
                pkgs.openssl
                pkgs.stdenv.cc.cc.lib
                pkgs.zlib
              ]
            );
          };
        }
      );
    };
}
