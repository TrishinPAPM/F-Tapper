#include "DigiKeyboard.h"

#define LED_PIN 1

static unsigned waitLeft = 0;
static unsigned extraUs = 0;

static void delayUs(unsigned us) {
  while (us--) _delay_us(1);
}

static void armWait() {
  unsigned lo = tapGapLo;
  unsigned hi = tapGapHi;
  if (hi < lo) {
    unsigned t = lo;
    lo = hi;
    hi = t;
  }
  if (lo < 20) lo = 20;
  if (hi < lo) hi = lo;
  waitLeft = (hi <= lo) ? lo : lo + (unsigned)random(hi - lo + 1);
  extraUs = (unsigned)random(1000);
}

void setup() {
  pinMode(LED_PIN, OUTPUT);
  for (uint8_t i = 0; i < 3; i++) {
    digitalWrite(LED_PIN, HIGH);
    _delay_ms(80);
    digitalWrite(LED_PIN, LOW);
    _delay_ms(80);
  }
  DigiKeyboard.begin();
  unsigned n = 2000;
  while (n--) {
    DigiKeyboard.update();
    _delay_ms(1);
  }
  hidCmdArmed = 1;
  randomSeed(analogRead(1));
}

void loop() {
  DigiKeyboard.update();
  if (hidDebounce) hidDebounce--;
  if (waitLeft) {
    if (extraUs) {
      unsigned u = extraUs;
      extraUs = 0;
      delayUs(u);
    }
    waitLeft--;
    digitalWrite(LED_PIN, LOW);
    _delay_ms(1);
    return;
  }
  if (tapEnabled) {
    digitalWrite(LED_PIN, HIGH);
    DigiKeyboard.sendKeyStroke(0);
    DigiKeyboard.sendKeyStroke(tapKey);
    digitalWrite(LED_PIN, LOW);
    armWait();
  } else {
    digitalWrite(LED_PIN, LOW);
    _delay_ms(1);
  }
}
