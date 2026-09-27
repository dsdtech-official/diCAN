

#ifndef DICAN_USB_H
#define DICAN_USB_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef struct dican_usb dican_usb;

// Opens the USB device.
int32_t dican_usb_open(const char *serial, dican_usb **device);

// Gets the input packet limit.
uint16_t dican_usb_max_packet_in(const dican_usb *device);

// Gets the output packet limit.
uint16_t dican_usb_max_packet_out(const dican_usb *device);

// Sends a USB control request.
int32_t dican_usb_control(dican_usb *device, uint8_t direction_in, uint8_t request, uint16_t value,
                          uint8_t *data, uint16_t length, uint32_t timeout_ms, uint32_t *transferred);

// Reads USB data.
int32_t dican_usb_read(dican_usb *device, uint8_t *buffer, uint32_t capacity, uint32_t *transferred);

// Writes USB data.
int32_t dican_usb_write(dican_usb *device, const uint8_t *buffer, uint32_t length, uint32_t timeout_ms);

// Aborts pending USB transfers.
int32_t dican_usb_abort(dican_usb *device);

// Closes the USB device.
void dican_usb_close(dican_usb *device);

#ifdef __cplusplus
}
#endif

#endif
