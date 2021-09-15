using System;
using System.Diagnostics;
using System.Linq;

using Serilog;

namespace Dalamud.Game.Internal.DXGI
{
    /// <summary>
    /// The address resolver for native D3D11 methods to facilitate displaying the Dalamud UI.
    /// </summary>
    public sealed class SwapChainSigResolver : BaseAddressResolver, ISwapChainAddressResolver
    {
        /// <inheritdoc/>
        public IntPtr Present { get; set; }

        /// <inheritdoc/>
        public IntPtr ResizeBuffers { get; set; }

        /// <inheritdoc/>
        protected override void Setup64Bit(SigScanner sig)
        {
            var module = Process.GetCurrentProcess().Modules.Cast<ProcessModule>().First(m => m.ModuleName == "dxgi.dll");

            Log.Debug($"Found DXGI: 0x{module.BaseAddress.ToInt64():X}");

            var scanner = new SigScanner(module);

            // This(code after the function head - offset of it) was picked to avoid running into issues with other hooks being installed into this function.
            this.Present = scanner.ScanModule("41 8B F0 8B FA 89 54 24 ?? 48 8B D9 48 89 4D ?? C6 44 24 ?? 00") - 0x37;

            //ResizeBuffers = scanner.ScanModule("45 8B CC 45 8B C5 33 D2 48 8B CF E8 ?? ?? ?? ?? 44 8B C0 48 8D 55 ?? 48 8D 4D ?? E8 ?? ?? ?? ??") - 0xAB;
            // ResizeBuffers = scanner.ScanModule("45 8B CC 45 8B C5 33 D2 48 8B CF E8 ?? ?? ?? ?? 44 8B C0 48 8D 55 ?? 48 8D 4D ?? E8 ?? ?? ?? ??") - 0x8E; // dxgi.dll 10.0.17763.1075
            //Search for the func start address of ResizeBuffers
            var ResizeBuffersSig = scanner.ScanModule("45 8B CC 45 8B C5 33 D2 48 8B CF E8 ?? ?? ?? ?? 44 8B C0 48 8D 55 ?? 48 8D 4D ?? E8 ?? ?? ?? ??");
            Log.Debug($"ResizeBuffersSig={ResizeBuffersSig.ToInt64():X}");
            //CC
            //CC
            //CC
            //CC
            //CC
            //.text: 00000001800206F0 48 8B C4                                                        mov rax, rsp
            //.text: 00000001800206F3 55                                                              push rbp
            //.text: 00000001800206F4 41 54                                                           push r12
            //.text: 00000001800206F6 41 55                                                           push r13
            //.text: 00000001800206F8 41 56                                                           push r14
            //.text: 00000001800206FA 41 57                                                           push r15
            this.ResizeBuffers = scanner.ScanReversed(ResizeBuffersSig, 0x100, "CC CC CC CC 48 8B C4 55 41 54") + 4;
            Log.Debug($"ResizeBuffers={ResizeBuffers.ToInt64():X}");
            Log.Debug($"ResizeBuffersOffset={ResizeBuffersSig.ToInt64() - ResizeBuffers.ToInt64():X}");

        }
    }
}
