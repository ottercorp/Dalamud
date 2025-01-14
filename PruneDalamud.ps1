Remove-Item -Force -Recurse .\scratch\*.pdb -ErrorAction SilentlyContinue
Remove-Item -Force -Recurse .\scratch\*.xml -ErrorAction SilentlyContinue
Remove-Item -Force -Recurse .\scratch\runtimes\linux-x64 -ErrorAction SilentlyContinue
Remove-Item -Force -Recurse .\scratch\runtimes\osx-x64 -ErrorAction SilentlyContinue
Remove-Item -Force -Recurse .\scratch\runtimes\unix -ErrorAction SilentlyContinue
#Remove-Item -Force -Recurse .\scratch\runtimes\win -ErrorAction SilentlyContinue
Remove-Item -Force -Recurse .\scratch\runtimes\win-x86 -ErrorAction SilentlyContinue
Remove-Item -Force -Recurse .\scratch\runtimes\alpine-x64 -ErrorAction SilentlyContinue
Remove-Item -Force -Recurse .\scratch\runtimes\linux-arm -ErrorAction SilentlyContinue
Remove-Item -Force -Recurse .\scratch\runtimes\linux-arm64 -ErrorAction SilentlyContinue
Remove-Item -Force -Recurse .\scratch\runtimes\linux-armel -ErrorAction SilentlyContinue
Remove-Item -Force -Recurse .\scratch\runtimes\linux-mips64 -ErrorAction SilentlyContinue
Remove-Item -Force -Recurse .\scratch\runtimes\linux-musl-x64 -ErrorAction SilentlyContinue
Remove-Item -Force -Recurse .\scratch\runtimes\linux-x86 -ErrorAction SilentlyContinue
Remove-Item -Force -Recurse .\scratch\runtimes\win-arm -ErrorAction SilentlyContinue
Remove-Item -Force -Recurse .\scratch\runtimes\win-arm64 -ErrorAction SilentlyContinue
Remove-Item -Force -Recurse .\scratch\hashes.json -ErrorAction SilentlyContinue

#这玩意action跑的时候用的是distrib那边的版本……