

#include "dican_usb.h"

#include <CoreFoundation/CoreFoundation.h>
#include <IOKit/IOCFPlugIn.h>
#include <IOKit/IOKitLib.h>
#include <IOKit/usb/IOUSBLib.h>
#include <stdlib.h>
#include <string.h>
#include <strings.h>

// Stores USB device state.
struct dican_usb
{
    IOUSBInterfaceInterface300 **interface;
    uint8_t interface_number;
    uint8_t pipe_in;
    uint8_t pipe_out;
    uint16_t packet_in;
    uint16_t packet_out;
};

// Matches the USB interface.
static int is_vendor_interface_of(io_service_t service, const char *serial)
{
    long interface_class = -1;
    CFTypeRef value = IORegistryEntryCreateCFProperty(service, CFSTR("bInterfaceClass"), kCFAllocatorDefault, 0);

    if (value != NULL && CFGetTypeID(value) == CFNumberGetTypeID())
    {
        CFNumberGetValue((CFNumberRef)value, kCFNumberLongType, &interface_class);
    }
    if (value != NULL)
    {
        CFRelease(value);
    }
    if (interface_class != 0xFF)
    {
        return 0;
    }

    int matches = 0;
    char text[256];
    CFTypeRef number = IORegistryEntrySearchCFProperty(service, kIOServicePlane, CFSTR("USB Serial Number"),
                                                       kCFAllocatorDefault,
                                                       kIORegistryIterateRecursively | kIORegistryIterateParents);

    if (number != NULL && CFGetTypeID(number) == CFStringGetTypeID() &&
        CFStringGetCString((CFStringRef)number, text, sizeof text, kCFStringEncodingUTF8))
    {
        matches = strcasecmp(text, serial) == 0;
    }
    if (number != NULL)
    {
        CFRelease(number);
    }
    return matches;
}

// Finds bulk transfer pipes.
static IOReturn find_bulk_pipes(struct dican_usb *device)
{
    UInt8 count = 0;
    IOReturn kr = (*device->interface)->GetNumEndpoints(device->interface, &count);

    if (kr != kIOReturnSuccess)
    {
        return kr;
    }

    for (UInt8 pipe = 1; pipe <= count; pipe++)
    {
        UInt8 direction = 0, number = 0, type = 0, interval = 0;
        UInt16 packet = 0;

        if ((*device->interface)->GetPipeProperties(device->interface, pipe, &direction, &number, &type, &packet,
                                                    &interval) != kIOReturnSuccess ||
            type != kUSBBulk)
        {
            continue;
        }

        if (direction == kUSBIn && device->pipe_in == 0)
        {
            device->pipe_in = pipe;
            device->packet_in = packet;
        }
        else if (direction == kUSBOut && device->pipe_out == 0)
        {
            device->pipe_out = pipe;
            device->packet_out = packet;
        }
    }

    return device->pipe_in != 0 && device->pipe_out != 0 ? kIOReturnSuccess : kIOReturnUnsupported;
}

// Opens the USB device.
int32_t dican_usb_open(const char *serial, dican_usb **device)
{
    if (serial == NULL || device == NULL)
    {
        return kIOReturnBadArgument;
    }
    *device = NULL;

    io_iterator_t iterator = 0;
    kern_return_t kr = IOServiceGetMatchingServices(kIOMainPortDefault, IOServiceMatching("IOUSBHostInterface"), &iterator);

    if (kr != KERN_SUCCESS)
    {
        return kr;
    }

    io_service_t found = 0;
    io_service_t service;

    while ((service = IOIteratorNext(iterator)) != 0)
    {
        if (found == 0 && is_vendor_interface_of(service, serial))
        {
            found = service;
        }
        else
        {
            IOObjectRelease(service);
        }
    }
    IOObjectRelease(iterator);

    if (found == 0)
    {
        return kIOReturnNotFound;
    }

    IOCFPlugInInterface **plugin = NULL;
    SInt32 score = 0;
    kr = IOCreatePlugInInterfaceForService(found, kIOUSBInterfaceUserClientTypeID, kIOCFPlugInInterfaceID, &plugin, &score);
    IOObjectRelease(found);

    if (kr != KERN_SUCCESS || plugin == NULL)
    {
        return kr != KERN_SUCCESS ? kr : kIOReturnNoResources;
    }

    IOUSBInterfaceInterface300 **interface = NULL;
    HRESULT result = (*plugin)->QueryInterface(plugin, CFUUIDGetUUIDBytes(kIOUSBInterfaceInterfaceID300), (LPVOID *)&interface);
    IODestroyPlugInInterface(plugin);

    if (result != S_OK || interface == NULL)
    {
        return kIOReturnUnsupported;
    }

    kr = (*interface)->USBInterfaceOpen(interface);
    if (kr != kIOReturnSuccess)
    {
        (*interface)->Release(interface);
        return kr;
    }

    struct dican_usb *opened = calloc(1, sizeof *opened);
    if (opened == NULL)
    {
        (*interface)->USBInterfaceClose(interface);
        (*interface)->Release(interface);
        return kIOReturnNoMemory;
    }
    opened->interface = interface;

    UInt8 number = 0;
    (*interface)->GetInterfaceNumber(interface, &number);
    opened->interface_number = number;

    kr = find_bulk_pipes(opened);
    if (kr != kIOReturnSuccess)
    {
        dican_usb_close(opened);
        return kr;
    }

    *device = opened;
    return kIOReturnSuccess;
}

// Gets the input packet limit.
uint16_t dican_usb_max_packet_in(const dican_usb *device)
{
    return device != NULL ? device->packet_in : 0;
}

// Gets the output packet limit.
uint16_t dican_usb_max_packet_out(const dican_usb *device)
{
    return device != NULL ? device->packet_out : 0;
}

// Sends a USB control request.
int32_t dican_usb_control(dican_usb *device, uint8_t direction_in, uint8_t request, uint16_t value,
                          uint8_t *data, uint16_t length, uint32_t timeout_ms, uint32_t *transferred)
{
    if (transferred != NULL)
    {
        *transferred = 0;
    }
    if (device == NULL || (length != 0 && data == NULL))
    {
        return kIOReturnBadArgument;
    }

    IOUSBDevRequestTO setup;
    memset(&setup, 0, sizeof setup);
    setup.bmRequestType = USBmakebmRequestType(direction_in ? kUSBIn : kUSBOut, kUSBVendor, kUSBInterface);
    setup.bRequest = request;
    setup.wValue = value;

    setup.wIndex = device->interface_number;
    setup.wLength = length;
    setup.pData = length != 0 ? data : NULL;
    setup.noDataTimeout = timeout_ms;
    setup.completionTimeout = timeout_ms;

    IOReturn kr = (*device->interface)->ControlRequestTO(device->interface, 0, &setup);

    if (transferred != NULL)
    {
        *transferred = setup.wLenDone;
    }
    return kr;
}

// Reads USB data.
int32_t dican_usb_read(dican_usb *device, uint8_t *buffer, uint32_t capacity, uint32_t *transferred)
{
    if (transferred != NULL)
    {
        *transferred = 0;
    }
    if (device == NULL || buffer == NULL || transferred == NULL)
    {
        return kIOReturnBadArgument;
    }

    UInt32 size = capacity;
    IOReturn kr = (*device->interface)->ReadPipe(device->interface, device->pipe_in, buffer, &size);

    if (kr == kIOReturnSuccess)
    {
        *transferred = size;
    }
    else if (kr == kIOUSBPipeStalled)
    {
        (*device->interface)->ClearPipeStallBothEnds(device->interface, device->pipe_in);
    }
    return kr;
}

// Writes USB data.
int32_t dican_usb_write(dican_usb *device, const uint8_t *buffer, uint32_t length, uint32_t timeout_ms)
{
    if (device == NULL || (length != 0 && buffer == NULL))
    {
        return kIOReturnBadArgument;
    }

    IOReturn kr = (*device->interface)->WritePipeTO(device->interface, device->pipe_out, (void *)buffer, length,
                                                    timeout_ms, timeout_ms);

    if (kr == kIOUSBTransactionTimeout || kr == kIOUSBPipeStalled)
    {
        (*device->interface)->ClearPipeStallBothEnds(device->interface, device->pipe_out);
    }
    return kr;
}

// Aborts pending USB transfers.
int32_t dican_usb_abort(dican_usb *device)
{
    if (device == NULL)
    {
        return kIOReturnBadArgument;
    }

    IOReturn in = (*device->interface)->AbortPipe(device->interface, device->pipe_in);
    IOReturn out = (*device->interface)->AbortPipe(device->interface, device->pipe_out);
    return in != kIOReturnSuccess ? in : out;
}

// Closes the USB device.
void dican_usb_close(dican_usb *device)
{
    if (device == NULL)
    {
        return;
    }
    if (device->interface != NULL)
    {
        (*device->interface)->USBInterfaceClose(device->interface);
        (*device->interface)->Release(device->interface);
    }
    free(device);
}
