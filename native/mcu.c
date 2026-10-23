/*
 * mcu - a bare 8086 microcontroller: the brain of a missile (or any small
 * device that does not need a whole PC).
 *
 * Just the CPU, its memory and a window of I/O ports backed by registers,
 * like the ship's bus card: no BIOS, no DOS, no timer chip. The firmware is
 * loaded like a .COM program (CS = DS = ES = SS, IP = 100h) and sets up its
 * own interrupt vector; the game raises that interrupt once a step (the
 * device's clock) and runs the CPU for the step's share of instructions.
 * Between interrupts the firmware sits in HLT and costs nothing.
 *
 * No callbacks into the game: port accesses stay inside this library.
 */
#include <stdlib.h>
#include <string.h>
#include "vm86.h"

#ifdef _WIN32
#  define MCU_API __declspec(dllexport)
#else
#  define MCU_API
#endif

#define MCU_PORTS   32
#define MCU_SEGMENT 0x1000

typedef struct mcu {
    vm86_t *cpu;
    uint16_t base;
    uint8_t regs[MCU_PORTS];
    uint8_t read[MCU_PORTS];
    uint8_t written[MCU_PORTS];
} mcu_t;

static uint8_t m_in(void *user, uint16_t port)
{
    mcu_t *m = (mcu_t *)user;
    uint16_t i = (uint16_t)(port - m->base);
    if (port < m->base || i >= MCU_PORTS) return 0xFF;
    m->read[i] = 1;
    return m->regs[i];
}

static void m_out(void *user, uint16_t port, uint8_t value)
{
    mcu_t *m = (mcu_t *)user;
    uint16_t i = (uint16_t)(port - m->base);
    if (port < m->base || i >= MCU_PORTS) return;
    m->regs[i] = value;
    m->written[i] = 1;
}

/* Firmware of up to 65280 bytes; ports base .. base+31. NULL if it does not fit. */
MCU_API mcu_t *mcu_create(const uint8_t *firmware, size_t len, uint16_t base)
{
    vm86_host_t host;
    mcu_t *m = (mcu_t *)calloc(1, sizeof(mcu_t));
    if (!m) return NULL;
    m->cpu = vm86_create();
    if (!m->cpu || vm86_load_com(m->cpu, firmware, len, MCU_SEGMENT) != 0) {
        if (m->cpu) vm86_destroy(m->cpu);
        free(m);
        return NULL;
    }
    m->base = base;
    memset(&host, 0, sizeof host);
    host.user = m;
    host.in8 = m_in;
    host.out8 = m_out;
    vm86_set_host(m->cpu, &host);
    vm86_run(m->cpu, 100000);           /* power on: the start-up, until it waits for a tick */
    return m;
}

MCU_API void mcu_destroy(mcu_t *m)
{
    if (!m) return;
    vm86_destroy(m->cpu);
    free(m);
}

MCU_API uint8_t *mcu_regs(mcu_t *m) { return m->regs; }
MCU_API uint8_t *mcu_read_flags(mcu_t *m) { return m->read; }
MCU_API uint8_t *mcu_written_flags(mcu_t *m) { return m->written; }

/* Raises interrupt 'vector' and runs up to 'steps' instructions (fewer once
 * the firmware halts). Returns the instructions run. */
MCU_API uint32_t mcu_step(mcu_t *m, uint8_t vector, uint32_t steps)
{
    vm86_request_interrupt(m->cpu, vector);
    return vm86_run(m->cpu, steps);
}

/* 0 running or waiting in HLT; otherwise the CPU stopped (an unsupported
 * instruction: the firmware crashed). */
MCU_API int mcu_failed(mcu_t *m)
{
    vm86_status_t s = vm86_status(m->cpu);
    return s != VM86_RUNNING && s != VM86_HALTED;
}
