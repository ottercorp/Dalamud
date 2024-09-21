namespace Dalamud.Game.Network;

/// <summary>
/// The address resolver for the <see cref="GameNetwork"/> class.
/// </summary>
internal sealed class GameNetworkAddressResolver : BaseAddressResolver
{
    /// <summary>
    /// Gets the address of the ProcessZonePacketDown method.
    /// </summary>
    public IntPtr ProcessZonePacketDown { get; private set; }

    /// <summary>
    /// Gets the address of the ProcessZonePacketUp method.
    /// </summary>
    public IntPtr ProcessZonePacketUp { get; private set; }
    
    /// <inheritdoc/>
    protected override void Setup64Bit(ISigScanner sig) {
        //this.ProcessZonePacketDown = sig.ScanText("40 53 56 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 44 24 ?? 8B F2");
        this.ProcessZonePacketDown = sig.ScanText("40 55 56 57 48 8D 6C 24 ?? 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 45 3F 8B FA"); //CN7.0
        this.ProcessZonePacketUp = sig.ScanText("48 89 5C 24 ?? 48 89 74 24 ?? 4C 89 64 24 ?? 55 41 56 41 57 48 8B EC 48 83 EC 70");
    }
}
