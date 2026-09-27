namespace DiCAN.Core.Devices;

// Discovers USB CAN adapters.
public interface ICanDeviceEnumerator
{

    // Lists the available items.
    IReadOnlyList<CanDeviceInfo> Enumerate();
}
