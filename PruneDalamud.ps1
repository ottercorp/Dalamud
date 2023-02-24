Remove-Item -Force -Recurse .\scratch\*.pdb -ErrorAction SilentlyContinue
Remove-Item -Force -Recurse .\scratch\*.xml -ErrorAction SilentlyContinue
Remove-Item -Force -Recurse .\scratch\runtimes\linux-x64 -ErrorAction SilentlyContinue
Remove-Item -Force -Recurse .\scratch\runtimes\osx-x64 -ErrorAction SilentlyContinue
Remove-Item -Force -Recurse .\scratch\runtimes\unix -ErrorAction SilentlyContinue
Remove-Item -Force -Recurse .\scratch\runtimes\win -ErrorAction SilentlyContinue
Remove-Item -Force -Recurse .\scratch\runtimes\win-x86 -ErrorAction SilentlyContinue
Remove-Item -Force -Recurse .\scratch\hashes.json -ErrorAction SilentlyContinue