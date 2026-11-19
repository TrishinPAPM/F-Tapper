/*
 * Based on Obdev's AVRUSB code and under the same license.
 *
 * TODO: Make a proper file header. :-)
 * Modified for Digispark by Digistump
 */
#ifndef __DigiKeyboard_h__
#define __DigiKeyboard_h__

#include <Arduino.h>
#include <avr/pgmspace.h>
#include <avr/interrupt.h>
#include <util/delay.h>
#include <string.h>

#include "usbdrv.h"
#include "keylayouts.h"

typedef uint8_t byte;

#define TEST_STRING "abcdefghijklmnopqrstuvwxyz ABCDEFGHIJKLMNOPQRSTUVWXYZ 1234567890 !\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~"

#define BUFFER_SIZE 2 // Minimum of 2: 1 for modifiers + 1 for keystroke

static uchar idleRate;           // in 4 ms units
volatile uchar hidLeds = 0;
volatile uchar hidLedsUpdated = 0;
volatile uchar hidCmdArmed = 0;
volatile uchar hidDebounce = 0;
volatile bool tapEnabled = false;
volatile uchar tapKey = 9;
volatile uchar tapSpeed = 5;
volatile unsigned tapGapLo = 130;
volatile unsigned tapGapHi = 310;
const uchar fwStamp[8] PROGMEM = { 0x46, 0x54, 0x41, 0x50, 0xA5, 0x00, 0x00, 0x00 };
static uchar fwIdReply[4];

const PROGMEM uchar usbHidReportDescriptor[USB_CFG_HID_REPORT_DESCRIPTOR_LENGTH] = {
        0x05, 0x01,                    // USAGE_PAGE (Generic Desktop)
        0x09, 0x06,                    // USAGE (Keyboard)
        0xa1, 0x01,                    // COLLECTION (Application)
        0x05, 0x07,                    //   USAGE_PAGE (Keyboard)
        0x19, 0xe0,                    //   USAGE_MINIMUM (LeftControl)
        0x29, 0xe7,                    //   USAGE_MAXIMUM (Right GUI)
        0x15, 0x00,                    //   LOGICAL_MINIMUM (0)
        0x25, 0x01,                    //   LOGICAL_MAXIMUM (1)
        0x75, 0x01,                    //   REPORT_SIZE (1)
        0x95, 0x08,                    //   REPORT_COUNT (8)
        0x81, 0x02,                    //   INPUT (modifiers)
        0x95, 0x01,                    //   REPORT_COUNT (1)
        0x75, 0x08,                    //   REPORT_SIZE (8)
        0x25, 0x73,                    //   LOGICAL_MAXIMUM (115)
        0x19, 0x00,                    //   USAGE_MINIMUM
        0x29, 0x73,                    //   USAGE_MAXIMUM
        0x81, 0x00,                    //   INPUT (key)
        0x95, 0x05,                    //   REPORT_COUNT (5)
        0x75, 0x01,                    //   REPORT_SIZE (1)
        0x05, 0x08,                    //   USAGE_PAGE (LEDs)
        0x19, 0x01,                    //   USAGE_MINIMUM (Num Lock)
        0x29, 0x05,                    //   USAGE_MAXIMUM (Kana)
        0x91, 0x02,                    //   OUTPUT (LEDs)
        0x95, 0x01,                    //   REPORT_COUNT (1)
        0x75, 0x03,                    //   REPORT_SIZE (3)
        0x91, 0x03,                    //   OUTPUT (LED pad)
        0x95, 0x04,                    //   REPORT_COUNT (4)
        0x75, 0x08,                    //   REPORT_SIZE (8)
        0x15, 0x00,                    //   LOGICAL_MINIMUM (0)
        0x26, 0xff, 0x00,              //   LOGICAL_MAXIMUM (255)
        0x09, 0x01,                    //   USAGE (Num Lock)
        0xb1, 0x02,                    //   FEATURE (Data,Var,Abs)
        0xc0                           // END_COLLECTION
        };

#define MOD_CONTROL_LEFT    MODIFIERKEY_LEFT_CTRL
#define MOD_SHIFT_LEFT      MODIFIERKEY_LEFT_SHIFT
#define MOD_ALT_LEFT        MODIFIERKEY_LEFT_ALT
#define MOD_GUI_LEFT        MODIFIERKEY_LEFT_GUI
#define MOD_CONTROL_RIGHT   MODIFIERKEY_RIGHT_CTRL
#define MOD_SHIFT_RIGHT     MODIFIERKEY_RIGHT_SHIFT
#define MOD_ALT_RIGHT       MODIFIERKEY_RIGHT_ALT
#define MOD_GUI_RIGHT       MODIFIERKEY_RIGHT_GUI

class DigiKeyboardDevice: public Print {
public:
    DigiKeyboardDevice() {
    }

    void begin() {
        noInterrupts();
        usbDeviceDisconnect();
        _delay_ms(250);
        usbDeviceConnect();
        usbInit();
        interrupts();
        memset(reportBuffer, 0, sizeof(reportBuffer));
        usbSetInterrupt(reportBuffer, sizeof(reportBuffer));
    }

    void update() {
        usbPoll();
    }

    // delay while updating until we are finished delaying
    void delay(long milli) {
        unsigned long last = millis();
        while (milli > 0) {
            unsigned long now = millis();
            milli -= now - last;
            last = now;
            update();
        }
    }

    //sendKeyStroke: sends a key press AND release
    void sendKeyStroke(byte keyStroke) {
        sendKeyStroke(keyStroke, 0);
    }

    void enableLEDFeedback() {
        sUseFeedbackLed = true;
        pinMode(LED_BUILTIN, OUTPUT);
    }

    void disableLEDFeedback() {
        sUseFeedbackLed = false;
    }

    //sendKeyStroke: sends a key press AND release with modifiers
    void sendKeyStroke(byte keyStroke, byte modifiers) {
        sendKeyStroke(keyStroke, modifiers, false);
    }

    //sendKeyStroke: sends a key press AND release with modifiers
    void sendKeyStroke(byte keyStroke, byte modifiers, bool aUseFeedbackLed) {
        if (aUseFeedbackLed) {
            digitalWrite(LED_BUILTIN, HIGH);
        }
        sendKeyPress(keyStroke, modifiers);
        delay(20);
        sendKeyPress(0, 0);
        delay(20);
        if (aUseFeedbackLed) {
            digitalWrite(LED_BUILTIN, LOW);
        }
    }

    //sendKeyPress: sends a key press only - no release
    //to release the key, send again with keyPress=0
    void sendKeyPress(byte keyPress) {
        sendKeyPress(keyPress, 0);
    }

    //sendKeyPress: sends a key press only, with modifiers - no release
    //to release the key, send again with keyPress=0
    void sendKeyPress(byte keyPress, byte modifiers) {
        uint8_t tries = 0;
        while (!usbInterruptIsReady() && tries < 20) {
            usbPoll();
            _delay_ms(1);
            tries++;
        }
        if (tries >= 20) return;

        memset(reportBuffer, 0, sizeof(reportBuffer));
        reportBuffer[0] = modifiers;
        reportBuffer[1] = keyPress;
        usbSetInterrupt(reportBuffer, sizeof(reportBuffer));
    }

    uint8_t keycode_to_modifier(uint8_t keycode) {
        uint8_t modifier = 0;

#ifdef SHIFT_MASK
        if (keycode & SHIFT_MASK)
            modifier |= MODIFIERKEY_SHIFT;
#endif
#ifdef ALTGR_MASK
        if (keycode & ALTGR_MASK)
            modifier |= MODIFIERKEY_RIGHT_ALT;
#endif
#ifdef RCTRL_MASK
        if (keycode & RCTRL_MASK) modifier |= MODIFIERKEY_RIGHT_CTRL;
#endif
        return modifier;
    }

    /*
     * Mask keycodes to ascii subset (+ a few F keys)
     */
    uint8_t keycode_to_key(uint8_t keycode) {
        uint8_t key = keycode & KEYCODE_MASK_SCANCODE;
        // the only valid ASCII code which has a scancode > 63
        if (key == KEY_NON_US_BS_MAPPING) {
            key = (uint8_t) KEY_NON_US_BS;
        }
        return key;
    }

    /*
     * Convert ASCII to USB code
     */
    size_t write(uint8_t chr) {
        uint8_t data = 0;
        if (chr == '\b') {
            data = (uint8_t) KEY_BACKSPACE; // 0x08
        } else if (chr == '\t') {
            data = (uint8_t) KEY_TAB;       // 0x09
        } else if (chr == '\n') {
            data = (uint8_t) KEY_ENTER;     // 0x0A
        } else if (chr == '\r') {
            data = (uint8_t) KEY_ENTER;     // 0x0D
        } else if (chr >= 0x20) {
            // read from mapping table
            data = pgm_read_byte_near(keycodes_ascii + (chr - 0x20));
        }
        if (data) {
            sendKeyStroke(keycode_to_key(data), keycode_to_modifier(data), sUseFeedbackLed);
        }
        return 1;
    }

    bool sUseFeedbackLed = false;
    uchar reportBuffer[2];
    using Print::write;
};

DigiKeyboardDevice DigiKeyboard = DigiKeyboardDevice();

#ifdef __cplusplus
extern "C" {
#endif
// USB_PUBLIC uchar usbFunctionSetup
uchar usbFunctionSetup(uchar data[8]) {
    usbRequest_t *rq = (usbRequest_t*) ((void*) data);

    usbMsgPtr = DigiKeyboard.reportBuffer; //
    if ((rq->bmRequestType & USBRQ_TYPE_MASK) == USBRQ_TYPE_CLASS) {
        /* class request type */

        if (rq->bRequest == USBRQ_HID_GET_REPORT) {
            if (rq->wValue.bytes[1] == 3) {
                fwIdReply[0] = pgm_read_byte(fwStamp + 5);
                fwIdReply[1] = pgm_read_byte(fwStamp + 6);
                fwIdReply[2] = pgm_read_byte(fwStamp + 7);
                fwIdReply[3] = tapEnabled ? 1 : 0;
                usbMsgPtr = fwIdReply;
                return 4;
            }
            return 0;
        } else if (rq->bRequest == USBRQ_HID_SET_IDLE) {
            idleRate = rq->wValue.bytes[1];
        } else if (rq->bRequest == USBRQ_HID_SET_REPORT) {
            return USB_NO_MSG;
        }
    } else {
        /* no vendor specific requests implemented */
    }

    return 0;
}

uchar usbFunctionWrite(uchar *data, uchar len) {
    if (len == 0) return 1;
    uchar cmd = data[0];
    uchar arg = (len >= 2) ? data[1] : 0;
    if (cmd == 0 && len >= 2) {
        cmd = data[1];
        arg = (len >= 3) ? data[2] : 0;
    }
    if (cmd == 0xA5) {
        if (arg != 0) tapKey = arg;
        return 1;
    }
    if (cmd == 0xA7) {
        uchar loB = 0;
        uchar hiB = 0;
        uchar found = 0;
        uchar i;
        for (i = 0; i + 2 < len; i++) {
            if (data[i] == 0xA7) {
                loB = data[i + 1];
                hiB = data[i + 2];
                found = 1;
                break;
            }
        }
        if (!found) return 1;
        if (loB < 2) loB = 2;
        if (hiB < loB) {
            uchar t = loB;
            loB = hiB;
            hiB = t;
        }
        tapGapLo = (unsigned)loB * 10;
        tapGapHi = (unsigned)hiB * 10;
        return 1;
    }
    if (cmd == 0xA6 && hidCmdArmed && hidDebounce == 0) {
        tapEnabled = !tapEnabled;
        hidDebounce = 250;
        return 1;
    }
    if (cmd == 0xA8 && hidCmdArmed) {
        tapEnabled = 0;
        hidDebounce = 250;
        return 1;
    }
    if (cmd == 0xA9 && hidCmdArmed) {
        tapEnabled = 1;
        hidDebounce = 250;
        return 1;
    }
    return 1;
}
#ifdef __cplusplus
} // extern "C"
#endif

#endif // __DigiKeyboard_h__
