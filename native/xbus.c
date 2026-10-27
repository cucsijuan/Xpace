/*
 * xbus - the ship's expansion bus as one ISA card for pc86.
 *
 * A window of I/O ports (16 per device slot) backed by plain registers that
 * the game reads and writes between machine steps: the game puts sensor
 * values in, the program reads them with IN; the program writes commands
 * with OUT, the game picks them up. Every access is also flagged, so the
 * game can see what was read (an acknowledge) or written since it last
 * looked. One IRQ line serves the whole bus.
 *
 * No callbacks into the game: port accesses stay inside this DLL.
 */
#include <stdlib.h>
#include <string.h>
#include "pc.h"

#ifdef _WIN32
#  define XBUS_API __declspec(dllexport)
#else
#  define XBUS_API
#endif

#define XBUS_MAX_PORTS 512

typedef struct xbus {
    pc_card_t card;
    uint16_t base, size;
    int irq_line;
    int irq_pending;
    uint8_t regs[XBUS_MAX_PORTS];
    uint8_t read[XBUS_MAX_PORTS];       /* 1 once IN read the port */
    uint8_t written[XBUS_MAX_PORTS];    /* 1 once OUT wrote the port */
} xbus_t;

/* What a saved state holds: everything after the card header. */
#define XBUS_STATE_OFFSET offsetof(xbus_t, irq_pending)
#define XBUS_STATE_SIZE   (sizeof(xbus_t) - XBUS_STATE_OFFSET)

static int x_in(void *user, uint16_t port, uint8_t *value)
{
    xbus_t *x = (xbus_t *)user;
    uint16_t i = (uint16_t)(port - x->base);
    if (port < x->base || i >= x->size) return PC_PORT_NOT_MINE;
    *value = x->regs[i];
    x->read[i] = 1;
    return PC_PORT_HANDLED;
}

static int x_out(void *user, uint16_t port, uint8_t value)
{
    xbus_t *x = (xbus_t *)user;
    uint16_t i = (uint16_t)(port - x->base);
    if (port < x->base || i >= x->size) return PC_PORT_NOT_MINE;
    x->regs[i] = value;
    x->written[i] = 1;
    return PC_PORT_HANDLED;
}

static int x_take_irq(void *user)
{
    xbus_t *x = (xbus_t *)user;
    if (!x->irq_pending) return -1;
    x->irq_pending = 0;
    return x->irq_line;
}

static void x_power(void *user, int on)
{
    xbus_t *x = (xbus_t *)user;
    (void)on;
    x->irq_pending = 0;
}

static size_t x_state_size(void *user) { (void)user; return XBUS_STATE_SIZE; }

static size_t x_save_state(void *user, void *buf, size_t len)
{
    if (len < XBUS_STATE_SIZE) return 0;
    memcpy(buf, (uint8_t *)user + XBUS_STATE_OFFSET, XBUS_STATE_SIZE);
    return XBUS_STATE_SIZE;
}

static int x_load_state(void *user, const void *buf, size_t len)
{
    if (len != XBUS_STATE_SIZE) return -1;
    memcpy((uint8_t *)user + XBUS_STATE_OFFSET, buf, XBUS_STATE_SIZE);
    return 0;
}

/* Ports base .. base+size-1 (size up to 512), interrupts on IRQ irq_line (2-7). */
XBUS_API xbus_t *xbus_create(uint16_t base, uint16_t size, int irq_line)
{
    xbus_t *x;
    if (size == 0 || size > XBUS_MAX_PORTS || irq_line < 2 || irq_line > 7) return NULL;
    x = (xbus_t *)calloc(1, sizeof(xbus_t));
    if (!x) return NULL;
    x->base = base;
    x->size = size;
    x->irq_line = irq_line;
    x->card.name = "xbus";
    x->card.user = x;
    x->card.power = x_power;
    x->card.port_in = x_in;
    x->card.port_out = x_out;
    x->card.take_irq = x_take_irq;
    x->card.state_size = x_state_size;
    x->card.save_state = x_save_state;
    x->card.load_state = x_load_state;
    return x;
}

XBUS_API void       xbus_destroy(xbus_t *x) { free(x); }
XBUS_API pc_card_t *xbus_slot(xbus_t *x) { return &x->card; }

/* The registers and the access flags, size bytes each; the game reads and
 * writes them directly (and clears the flags it has seen). */
XBUS_API uint8_t *xbus_regs(xbus_t *x) { return x->regs; }
XBUS_API uint8_t *xbus_read_flags(xbus_t *x) { return x->read; }
XBUS_API uint8_t *xbus_written_flags(xbus_t *x) { return x->written; }

/* Asks for the bus's interrupt; the machine takes it on its next slice. */
XBUS_API void xbus_raise_irq(xbus_t *x) { x->irq_pending = 1; }

/* ---------- text displays ---------- */

#include "font_cp437.h"

static const uint8_t CGA[16][3] = {
    {0x00, 0x00, 0x00}, {0x00, 0x00, 0xAA}, {0x00, 0xAA, 0x00}, {0x00, 0xAA, 0xAA},
    {0xAA, 0x00, 0x00}, {0xAA, 0x00, 0xAA}, {0xAA, 0x55, 0x00}, {0xAA, 0xAA, 0xAA},
    {0x55, 0x55, 0x55}, {0x55, 0x55, 0xFF}, {0x55, 0xFF, 0x55}, {0x55, 0xFF, 0xFF},
    {0xFF, 0x55, 0x55}, {0xFF, 0x55, 0xFF}, {0xFF, 0xFF, 0x55}, {0xFF, 0xFF, 0xFF},
};

/* Character and attribute pairs, as in CGA text memory (cols x rows), to
 * RGB pixels with the 8x16 CP437 font: cols*8 x rows*16 x 3 bytes. */
XBUS_API void xbus_render_text(const uint8_t *cells, int cols, int rows, uint8_t *rgb)
{
    int row, col, y, x, width = cols * 8;
    for (row = 0; row < rows; row++)
        for (col = 0; col < cols; col++) {
            uint8_t ch = cells[(row * cols + col) * 2];
            uint8_t attr = cells[(row * cols + col) * 2 + 1];
            const uint8_t *fg = CGA[attr & 0x0F], *bg = CGA[(attr >> 4) & 0x07];
            for (y = 0; y < 16; y++) {
                uint8_t bits = vga_font_8x16[ch][y];
                uint8_t *p = rgb + (((size_t)row * 16 + y) * width + (size_t)col * 8) * 3;
                for (x = 0; x < 8; x++, p += 3) {
                    const uint8_t *c = (bits & (0x80 >> x)) ? fg : bg;
                    p[0] = c[0]; p[1] = c[1]; p[2] = c[2];
                }
            }
        }
}

/* ---------- graphics displays ---------- */

/* How bright each of the four pixel values lights the phosphor. */
static const float LEVEL[4] = {0.0f, 0.3f, 0.6f, 1.0f};

/* A graphics screen to RGB pixels (width x height x 3 bytes). Memory holds
 * 2 bits a pixel, four to a byte with the leftmost in the top bits, rows of
 * width/4 bytes. The phosphor keeps glowing after the beam leaves: each
 * pixel shows the brighter of what memory says now and its old glow times
 * fade (0-1). glow (width*height floats) carries that from call to call;
 * colour is the phosphor's RGB (0-1). */
XBUS_API void xbus_render_graphics(const uint8_t *pixels, int width, int height,
                                   float *glow, float fade, const float *colour, uint8_t *rgb)
{
    int x, y, i;
    for (y = 0; y < height; y++)
        for (x = 0; x < width; x++) {
            uint8_t byte = pixels[y * (width / 4) + x / 4];
            float lit = LEVEL[(byte >> ((3 - (x & 3)) * 2)) & 3];
            float *g = &glow[y * width + x];
            uint8_t *p = rgb + ((size_t)y * width + x) * 3;
            *g = lit > *g * fade ? lit : *g * fade;
            for (i = 0; i < 3; i++) {
                float v = (0.03f + *g) * colour[i] * 255.0f;
                p[i] = (uint8_t)(v > 255.0f ? 255.0f : v);
            }
        }
}
