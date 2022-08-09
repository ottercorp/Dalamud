using System;

namespace Dalamud.Game.Network
{
    /// <summary>
    /// The address resolver for the <see cref="GameNetwork"/> class.
    /// </summary>
    public sealed class GameNetworkAddressResolver : BaseAddressResolver
    {
        /// <summary>
        /// Gets the address of the ProcessZonePacketDown method.
        /// </summary>
        public IntPtr ProcessZonePacketDown { get; private set; }

        /// <summary>
        /// Gets the address of the ProcessZonePacketUp method.
        /// </summary>
        public IntPtr ProcessZonePacketUp { get; private set; }

        protected override void Setup64Bit(SigScanner sig) {
            this.ProcessZonePacketDown = sig.ScanText("40 55 56 57 48 8D 6C 24 ?? 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 45 ?? 8B ?? 49 8B ??"); // CN
            this.ProcessZonePacketUp =
                sig.ScanText("48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 41 56 41 57 48 83 EC 70 8B 81 ?? ?? ?? ??");
        }
    }
}
