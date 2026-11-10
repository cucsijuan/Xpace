/* registry.c - REGISTRY 1.0, a station's registry terminal (REGISTRY.COM)
 *
 * Shows the station's traffic logbook: the ships that docked lately, where and
 * how long ago. Alt+F10 opens the service session, behind the service key;
 * in it the registry's core can be read and its signature records copied out
 * to a ship's hold.
 *
 * The registry card (16 ports at 100h): +0 type 30  +2 status (bit 0 online,
 * 1 a ship of the user's in a hangar, 2 new signatures, 3 service locked,
 * 4 session open, 5 the last session was traced, 6 waiting)  +3 the intrusion
 * watch, 0 to 255  +4 write: 1 open, 2 wrong key, 3 carve at +6, 4 write to the
 * hold, 5 close  +6 offset (word)  +8 the last carve (1 carved, 2 again, 3 bad)
 * +9 carved  +10 tonnes written (255 none)  +11 logbook entries  +12 core rows
 * (word)  +14 key digits  +15 signatures to a tonne.
 * Memory at D000h: +1000h logbook (16 B each: callsign 8, hangar, pad,
 * minutes ago), +1200h station name, +1300h service key, +1340h the station's
 * last word, +2000h the core.
 *
 * Build: SMALLC REGISTRY.C
 */
#include <conio.h>
#include <dos.h>

#define CARD        0x100
#define TYPE_REG    30
#define ONLINE      1
#define PRESENT     2
#define FRESH       4
#define LOCKED      8
#define OPEN        16
#define TRACED      32
#define WAITING     64
#define SEG         0xD000
#define LOGBOOK     0x1000
#define TITLE       0x1200
#define KEY         0x1300
#define WORDS       0x1340
#define CORE        0x2000
#define VIDEO       0xB800
#define COLS        80
#define ROWS        20          /* rows of the core on screen */
#define DIM         0x02
#define BRIGHT      0x0A
#define WHITE       0x0F
#define ALARM       0x0C
#define AMBER       0x0E
#define INVERSE     0x20
#define STEP        20          /* loop turns a matching digit of the key costs */

int seed;                       /* the program's own scheduling: noise in its timing */

/* Words on a row from a column, in a colour; the column after them */
at(r, col, words, attr)
int r, col, attr;
char *words;
{
    while (*words) {
        poke(VIDEO, (r * COLS + col) * 2, (attr << 8) | (*words & 255));
        words++;
        col++;
    }
    return col;
}

/* The same out of the card's memory, at most n characters */
far_at(r, col, off, n, attr)
int r, col, off, n, attr;
{
    int i, c;
    for (i = 0; i < n; i++) {
        c = peekb(SEG, off + i);
        if (c == 0)
            break;
        poke(VIDEO, (r * COLS + col + i) * 2, (attr << 8) | c);
    }
    return col + i;
}

/* A row of one character */
rule(r, c, attr)
int r, c, attr;
{
    int col;
    for (col = 0; col < COLS; col++)
        poke(VIDEO, (r * COLS + col) * 2, (attr << 8) | c);
}

clear()
{
    int r;
    for (r = 0; r < 25; r++)
        rule(r, ' ', DIM);
}

/* A number, right-aligned in a width */
num(r, col, value, width, attr)
int r, col, value, width, attr;
{
    int i;
    for (i = width - 1; i >= 0; i--) {
        poke(VIDEO, (r * COLS + col + i) * 2, (attr << 8) | (value > 0 || i == width - 1 ? '0' + value % 10 : ' '));
        value = value / 10;
    }
    return col + width;
}

hexdigit(n)
int n;
{
    n = n & 15;
    return n < 10 ? '0' + n : 'A' + n - 10;
}

hex(r, col, value, digits, attr)
int r, col, value, digits, attr;
{
    int i;
    for (i = digits - 1; i >= 0; i--) {
        poke(VIDEO, (r * COLS + col + i) * 2, (attr << 8) | hexdigit(value));
        value = value >> 4;
    }
    return col + digits;
}

/* The BIOS clock's ticks, 18 a second */
ticks()
{
    return peek(0x40, 0x6C);
}

wait(n)
int n;
{
    int from;
    from = ticks();
    while (ticks() - from < n)
        ;
}

/* Timer 0's count, latched: it counts down by 2 every 0.84 us */
pit()
{
    int lo, hi;
    outp(0x43, 0);
    lo = inp(0x40);
    hi = inp(0x40);
    return (hi << 8) | lo;
}

/* A key: its scan code in the high byte, its character in the low one; 0 none */
key()
{
    struct REGS in, out;
    if (!kbhit())
        return 0;
    in.ax = 0;
    int86(0x16, &in, &out);
    return out.ax;
}

status()
{
    return inp(CARD + 2);
}

/* The core's rows of 16 bytes */
core_rows()
{
    return inp(CARD + 12) | (inp(CARD + 13) << 8);
}

command(c)
int c;
{
    outp(CARD + 4, c);
}

/* Waits for the station's answer, up to a few seconds */
answer()
{
    int from;
    wait(2);
    from = ticks();
    while ((status() & WAITING) && ticks() - from < 90)
        ;
}

/* ---------- the logbook ---------- */

frame(heading)
char *heading;
{
    int col;
    clear();
    rule(0, ' ', INVERSE);
    col = far_at(0, 2, TITLE, 32, 0x2F);
    at(0, col + 1, "-", 0x2F);
    at(0, col + 3, heading, 0x2F);
    at(0, 66, "REGISTRY 1.0", 0x2F);
}

/* The logbook's screen; the rows are filled in by logbook_rows */
logbook()
{
    frame("TRAFFIC REGISTER");
    at(2, 2, "CALLSIGN      HANGAR   PAD   LOGGED", BRIGHT);
    rule(3, 196, DIM);
    rule(23, 196, DIM);
    at(24, 50, "PUBLIC ACCESS - READ ONLY", DIM);
}

logbook_rows()
{
    int i, n, row, minutes, s;
    s = status();
    n = inp(CARD + 11);
    for (i = 0; i < 16; i++) {
        row = 5 + i;
        rule(row, ' ', DIM);
        if (i == 0 && !(s & ONLINE))
            at(row, 2, "CONNECTING TO THE REGISTRY...", AMBER);
        else if (i == 0 && n == 0)
            at(row, 2, "NO DOCKINGS LOGGED.", DIM);
        if (i >= n)
            continue;
        far_at(row, 2, LOGBOOK + i * 16, 8, WHITE);
        num(row, 16, peekb(SEG, LOGBOOK + i * 16 + 8), 4, BRIGHT);
        num(row, 25, peekb(SEG, LOGBOOK + i * 16 + 9), 3, BRIGHT);
        minutes = peek(SEG, LOGBOOK + i * 16 + 12);
        if (minutes < 60) {
            num(row, 30, minutes, 3, DIM);
            at(row, 34, "MIN AGO", DIM);
        }
        else {
            num(row, 30, minutes / 60, 3, DIM);
            at(row, 34, "H", DIM);
            num(row, 35, minutes % 60, 3, DIM);
            at(row, 39, "MIN AGO", DIM);
        }
    }
    at(24, 2, s & ONLINE ? "TERMINAL READY  " : "TERMINAL OFFLINE", s & ONLINE ? BRIGHT : AMBER);
}

/* ---------- the service session ---------- */

/* The intrusion watch's bar along the bottom */
watch_bar(r)
int r;
{
    int i, n, attr;
    n = inp(CARD + 3) * 40 / 255;
    attr = n < 20 ? BRIGHT : n < 32 ? AMBER : ALARM;
    at(r, 2, "TRACE [", DIM);
    for (i = 0; i < 40; i++)
        poke(VIDEO, (r * COLS + 9 + i) * 2, ((i < n ? attr : DIM) << 8) | (i < n ? 219 : 250));
    at(r, 49, "]", DIM);
}

/* The station's last word on a row */
words(r, attr)
int r, attr;
{
    rule(r, ' ', DIM);
    far_at(r, 2, WORDS, 64, attr);
}

/* The session was traced: shown until a key */
traced()
{
    frame("SERVICE");
    at(10, 22, "*** CONNECTION TRACED ***", ALARM);
    words(12, WHITE);
    at(14, 22, "SERVICE LOCKED. PRESS ANY KEY.", DIM);
    while (!key())
        ;
}

/* Compares the key typed with the service key, a digit at a time: the time it took, in us */
check(typed, n, good)
char *typed;
int n, *good;
{
    int i, j, c, from, d, len, extra;
    len = inp(CARD + 14);
    from = pit();
    *good = n == len;
    for (i = 0; i < len; i++) {
        c = peekb(SEG, KEY + i);
        if (i >= n || typed[i] != c) {
            *good = 0;
            break;
        }
        for (j = 0; j < STEP; j++)
            ;
    }
    seed = seed * 25173 + 13849;
    extra = (seed >> 8) & 7;
    for (j = 0; j < extra; j++)
        ;
    d = from - pit();
    d = (d >> 1) & 0x7FFF;
    return d - d / 6;
}

login()
{
    char typed[17];
    int n, k, c, len, us, good, s;
    len = inp(CARD + 14);
    frame("SERVICE");
    at(3, 2, "SERVICE SESSION. AUTHORISED STAFF ONLY.", WHITE);
    at(4, 2, "ACCESS IS LOGGED AND WATCHED.", DIM);
    at(7, 2, "SERVICE KEY: ", BRIGHT);
    at(24, 2, "0-9 A-F  ENTER try  BACKSPACE  ESC log out", DIM);
    n = 0;
    for (;;) {
        s = status();
        if (!(s & OPEN)) {
            if (s & TRACED)
                traced();
            return 0;
        }
        watch_bar(22);
        for (c = 0; c < 16; c++)
            poke(VIDEO, (7 * COLS + 15 + c) * 2, ((c == n ? INVERSE : WHITE) << 8) | (c < n ? typed[c] : (c < len ? 250 : ' ')));
        k = key();
        if (k == 0)
            continue;
        c = k & 255;
        if (c == 27) {
            command(5);
            return 0;
        }
        if (c >= 'a' && c <= 'f')
            c = c - 32;
        if (((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F')) && n < len && n < 16) {
            typed[n] = c;
            n++;
        }
        else if (c == 8 && n > 0)
            n--;
        else if (c == 13) {
            us = check(typed, n, &good);
            if (good)
                return 1;
            command(2);
            rule(9, ' ', DIM);
            at(9, 2, "ACCESS DENIED", ALARM);
            at(9, 20, "RESPONSE", DIM);
            num(9, 29, us, 6, WHITE);
            at(9, 36, "US", DIM);
        }
    }
}

/* One row of the core: offset, 16 bytes, their characters */
dump_row(r, row, here)
int r, row, here;
{
    int i, b, off, attr;
    off = row * 16;
    attr = here ? INVERSE : DIM;
    rule(r, ' ', attr);
    hex(r, 1, off, 4, here ? 0x2F : BRIGHT);
    at(r, 5, ":", attr);
    for (i = 0; i < 16; i++) {
        b = peekb(SEG, CORE + off + i);
        hex(r, 7 + i * 3 + (i >= 8 ? 1 : 0), b, 2, here ? 0x2F : WHITE);
        poke(VIDEO, (r * COLS + 58 + i) * 2, ((here ? 0x2F : BRIGHT) << 8) | (b >= 32 && b < 127 ? b : 250));
    }
}

dump(top, cursor)
int top, cursor;
{
    int i, rows;
    rows = core_rows();
    for (i = 0; i < ROWS; i++) {
        if (top + i < rows)
            dump_row(2 + i, top + i, top + i == cursor);
        else
            rule(2 + i, ' ', DIM);
    }
}

/* The core: move through it, carve signatures, write them to the hold */
core()
{
    int rows, top, cursor, k, c, scan, n, tonnes, s, per;
    rows = core_rows();
    per = inp(CARD + 15);
    top = 0;
    cursor = 0;
    frame("SERVICE - REGISTRY CORE");
    at(1, 1, "OFFS   00 01 02 03 04 05 06 07  08 09 0A 0B 0C 0D 0E 0F   CHARACTERS", BRIGHT);
    at(24, 1, "ARROWS PGUP PGDN move  C carve record  W write to hold  ESC log out", DIM);
    dump(top, cursor);
    for (;;) {
        s = status();
        if (!(s & OPEN)) {
            if (s & TRACED)
                traced();
            return;
        }
        watch_bar(23);
        n = inp(CARD + 9);
        at(22, 2, "CARVED", DIM);
        num(22, 9, n, 3, WHITE);
        at(22, 13, "SIGNATURES =", DIM);
        num(22, 26, n / per, 3, WHITE);
        at(22, 30, "T", DIM);
        k = key();
        if (k == 0)
            continue;
        c = k & 255;
        scan = (k >> 8) & 255;
        if (c == 27) {
            command(5);
            return;
        }
        if (c == 0 || c == 0xE0) {
            if (scan == 0x48 && cursor > 0)
                cursor--;
            else if (scan == 0x50 && cursor < rows - 1)
                cursor++;
            else if (scan == 0x49)
                cursor = cursor > ROWS ? cursor - ROWS : 0;
            else if (scan == 0x51)
                cursor = cursor + ROWS < rows ? cursor + ROWS : rows - 1;
            if (cursor < top)
                top = cursor;
            if (cursor >= top + ROWS)
                top = cursor - ROWS + 1;
            dump(top, cursor);
            continue;
        }
        if (c == 'c' || c == 'C') {
            outp(CARD + 6, (cursor * 16) & 255);
            outp(CARD + 7, (cursor * 16) >> 8);
            command(3);
            wait(1);
            rule(21, ' ', DIM);
            c = inp(CARD + 8);
            if (c == 1) {
                at(21, 2, "SIGNATURE CARVED:", BRIGHT);
                far_at(21, 20, CORE + cursor * 16 + 4, 8, WHITE);
            }
            else if (c == 2)
                at(21, 2, "ALREADY CARVED.", AMBER);
            else
                at(21, 2, "NO GOOD RECORD THERE. CHECKSUM ERROR LOGGED.", ALARM);
        }
        else if (c == 'w' || c == 'W') {
            command(4);
            answer();
            tonnes = inp(CARD + 10);
            if (tonnes != 255) {
                frame("SERVICE");
                words(10, BRIGHT);
                at(14, 2, "SESSION CLOSED. PRESS ANY KEY.", DIM);
                while (!key())
                    ;
                return;
            }
            words(21, AMBER);
        }
    }
}

service()
{
    int s;
    command(1);
    answer();
    s = status();
    if (!(s & OPEN)) {
        frame("SERVICE");
        words(10, s & LOCKED ? ALARM : AMBER);
        at(14, 2, "PRESS ANY KEY.", DIM);
        while (!key())
            ;
        return;
    }
    if (login())
        core();
}

main()
{
    int k, scan, last;
    if (inp(CARD) != TYPE_REG) {
        cputs("REGISTRY: no registry card.\r\n");
        return 1;
    }
    seed = ticks();
    last = -100;
    logbook();
    for (;;) {
        if (ticks() - last >= 18 || ticks() < last) {
            logbook_rows();
            last = ticks();
        }
        k = key();
        if (k == 0)
            continue;
        scan = (k >> 8) & 255;
        if (scan == 0x71) {             /* Alt+F10: the BIOS keeps that Alt was down */
            service();
            logbook();
            last = -100;
        }
    }
}
