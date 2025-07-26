
{ pkgs ? import <nixpkgs> {} }:
let
  dotnet-with-icu = pkgs.dotnet-sdk.overrideAttrs (oldAttrs: {
    buildInputs = (oldAttrs.buildInputs or []) ++ [ pkgs.icu ];
  });
in
pkgs.mkShell {
  buildInputs = [
    dotnet-with-icu
    pkgs.icu
    pkgs.zlib
    pkgs.openssl
    pkgs.krb5
    pkgs.libunwind
  ];
  
  shellHook = ''
    # ICU configuration for .NET
    export LD_LIBRARY_PATH="${pkgs.icu}/lib:${pkgs.zlib}/lib:${pkgs.openssl.out}/lib:${pkgs.krb5}/lib:${pkgs.libunwind}/lib:$LD_LIBRARY_PATH"
    export ICU_DATA="${pkgs.icu}/share/icu"
    
    # .NET Performance optimizations
    export DOTNET_TieredCompilation=1
    export DOTNET_TieredPGO=1
    export DOTNET_TC_QuickJitForLoops=1
    export DOTNET_gcServer=1
    export DOTNET_gcConcurrent=1
    export DOTNET_NUGET_SIGNATURE_VERIFICATION=false
    
    # For debugging ICU issues
    export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
    
    echo "=== Shelltrac Development Environment ==="
    echo "ICU libraries: ${pkgs.icu}/lib"
    echo "LD_LIBRARY_PATH: $LD_LIBRARY_PATH"
    echo ""
    echo "Test with: dotnet run -c Release hello.s"
    echo "Profile with: time dotnet run -c Release hello.s"
  '';
}
