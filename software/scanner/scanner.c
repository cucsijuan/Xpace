/* scanner.c - StubCorp Cargo Scanner 1.1 (SCANNER.COM)
 *
 *   SCANNER       stays resident, an application of the desktop
 *   SCANNER OFF   takes it out of memory
 *
 * Reads the hold of the ship the radar has locked. SELECT starts a reading
 * and stops it; the lock must stay on that ship until it ends, or it starts
 * over. BACK goes back to the desktop. It draws one row of the screen a step.
 *
 * The scanner (16 ports):
 *
 *   240h  +0 type 22  +2 status (bit 0 reading, bit 1 a reading is in the
 *         window, bit 2 a ship locked, bit 3 the last one was broken off,
 *         bit 4 out of range)  +3 progress, 0 to 255  +4 write: 1 start,
 *         0 stop  +6 seconds left  +8 segment  +10 offset of the window.
 *         The window: +0 callsign (8 characters)  +8 capacity (t)
 *         +10 carried (t)  +12 kinds of good  +14 stolen (t)  +16 entries
 *         of 16 bytes: the good's name (12 characters), its tonnes, of them
 *         the stolen ones.
 *
 * Build: SMALLC -I. SCANNER.C
 */
#include "tsr.h"
#include "icons.h"

#define SCREEN      "COMMS "
#define SCANNER     0x240
#define TYPE_SCANNER 22
#define SIGN        "XPSC11"
#define MARK        'S'
#define COLS        32
#define ROWS        12
#define READING     1
#define HAS_READING 2
#define HAS_TARGET  4
#define BROKEN      8
#define OUT_OF_RANGE 16
#define ATTR_LABEL  0x03
#define ATTR_VALUE  0x0F
#define ATTR_TITLE  0x1F
#define ATTR_GOOD   0x0A
#define ATTR_WARN   0x0C
#define ATTR_BAR    0x70
#define BAR_CELLS   20
#define GOODS_ROW   7
#define GOODS_SHOWN 4

int open;
int alone;                              /* no desktop: the screen is always ours */
int row;                                /* the next row to draw */
int status;
unsigned window_seg;
unsigned window;

/* Words on a row from a column; the column after them */
put(r, col, words, attr)
int r, col, attr;
char *words;
{
    unsigned off;
    off = (r * COLS + col) * 2;
    while (*words) {
        poke(screen_seg, off, (attr << 8) | (*words & 255));
        off = off + 2;
        words++;
        col++;
    }
    return col;
}

/* A number, from a column; the column after it */
put_number(r, col, n)
int r, col;
unsigned n;
{
    char digits[6];
    int i;
    i = 5;
    digits[5] = 0;
    do {
        i--;
        digits[i] = '0' + n % 10;
        n = n / 10;
    } while (n && i);
    return put(r, col, digits + i, ATTR_VALUE);
}

clear_row(r, attr)
int r, attr;
{
    far_fill(screen_seg, r * COLS * 2, (attr << 8) | ' ', COLS);
}

/* Characters from the window, count of them, on a row */
put_window(r, col, at, count, attr)
int r, col, count, attr;
unsigned at;
{
    int i;
    for (i = 0; i < count; i++)
        poke(screen_seg, (r * COLS + col + i) * 2, (attr << 8) | peekb(window_seg, window + at + i));
}

draw_state(r)
int r;
{
    int i, cells;
    if (status & READING) {
        put(r, 1, "READING", ATTR_LABEL);
        cells = inp(SCANNER + 3) * BAR_CELLS / 255;
        for (i = 0; i < BAR_CELLS; i++)
            poke(screen_seg, (r * COLS + 9 + i) * 2, ((i < cells ? ATTR_BAR : ATTR_LABEL) << 8) | (i < cells ? ' ' : '.'));
    } else if (status & BROKEN)
        put(r, 1, "LOCK LOST: START OVER", ATTR_WARN);
    else
        put(r, 1, "READY", ATTR_LABEL);
}

draw_row(r)
int r;
{
    int n, col;
    unsigned stolen;
    if (r == 0) {
        clear_row(r, ATTR_TITLE);
        put(r, 1, "CARGO SCANNER 1.1", ATTR_TITLE);
        return;
    }
    clear_row(r, ATTR_LABEL);
    if (r == 2) {
        put(r, 1, "RADAR", ATTR_LABEL);
        if (status & OUT_OF_RANGE)
            put(r, 9, "OUT OF RANGE", ATTR_WARN);
        else if (status & HAS_TARGET)
            put(r, 9, "SHIP LOCKED", ATTR_GOOD);
        else
            put(r, 9, "NO LOCK", ATTR_WARN);
    } else if (r == 3) {
        draw_state(r);
    } else if (r == 4 && (status & READING)) {
        col = put(r, 1, "LEFT", ATTR_LABEL);
        col = put_number(r, 9, inpw(SCANNER + 6));
        put(r, col, " s", ATTR_LABEL);
    } else if (r == 5) {
        if (status & HAS_READING) {
            put(r, 1, "SHIP", ATTR_LABEL);
            put_window(r, 9, 0, 8, ATTR_VALUE);
        } else
            put(r, 1, "NO READING YET", ATTR_LABEL);
    } else if (r == 6 && (status & HAS_READING)) {
        put(r, 1, "HOLD", ATTR_LABEL);
        col = put_number(r, 9, peek(window_seg, window + 10));
        col = put(r, col, " / ", ATTR_LABEL);
        col = put_number(r, col, peek(window_seg, window + 8));
        col = put(r, col, " t", ATTR_LABEL);
        stolen = peek(window_seg, window + 14);
        if (stolen) {
            col = put_number(r, col + 2, stolen);
            put(r, col, " STOLEN", ATTR_WARN);
        }
    } else if (r >= GOODS_ROW && r < GOODS_ROW + GOODS_SHOWN && (status & HAS_READING)) {
        n = r - GOODS_ROW;
        if (n == 0 && peek(window_seg, window + 10) == 0)
            put(r, 1, "NOTHING ABOARD", ATTR_GOOD);
        else if (n < peekb(window_seg, window + 12)) {
            put_window(r, 1, 16 + n * 16, 12, ATTR_GOOD);
            col = put_number(r, 15, peek(window_seg, window + 16 + n * 16 + 12));
            col = put(r, col, " t", ATTR_LABEL);
            stolen = peek(window_seg, window + 16 + n * 16 + 14);
            if (stolen) {
                col = put_number(r, col + 1, stolen);
                put(r, col, " STOLEN", ATTR_WARN);
            }
        }
    } else if (r == ROWS - 1) {
        put(r, 1, status & READING ? "SELECT: STOP" : "SELECT: SCAN", ATTR_LABEL);
        if (!alone)
            put(r, 19, "BACK: DESK", ATTR_LABEL);
    }
}

press(key)
int key;
{
    if (key == KEY_SELECT)
        outp(SCANNER + 4, (status & READING) ? 0 : 1);
    else if (key == KEY_BACK && !alone)
        give_to(0);
}

tick()
{
    int key;
    key = next_key();
    if (owner() != MARK) {
        open = 0;
        return;
    }
    if (!open) {
        open = 1;
        row = 0;
        screen_mode(0);
        return;
    }
    status = inp(SCANNER + 2);
    if (key)
        press(key);
    draw_row(row);
    row++;
    if (row == ROWS)
        row = 0;
}

main(argc, argv)
int argc;
char **argv;
{
    unsigned seg;
    if (argc > 1 && (argv[1][0] == 'O' || argv[1][0] == 'o')) {
        seg = find_resident(SIGN);
        if (seg == 0) {
            say("Scanner program is not resident.\r\n$");
            return 1;
        }
        leave_desk(MARK);
        if (find_screen(SCREEN) && owner() == MARK)
            give_to(0);
        find_resident(SIGN);
        unlink(seg);
        free_program(seg);
        say("Scanner program removed.\r\n$");
        return 0;
    }
    if (find_resident(SIGN)) {
        say("Scanner program is already resident.\r\n$");
        return 1;
    }
    if (inp(SCANNER) != TYPE_SCANNER) {
        say("SCANNER: the ship has no cargo scanner.\r\n$");
        return 1;
    }
    if (!find_screen(SCREEN) || peekb(card_seg, entry + 7) != COLS || peekb(card_seg, entry + 8) < ROWS) {
        say("SCANNER: the ship has no COMMS screen (32 x 12).\r\n$");
        return 1;
    }
    window_seg = inpw(SCANNER + 8);
    window = inpw(SCANNER + 10);
    if (!join_desk(MARK, "Scanner", icon_scanner)) {
        alone = 1;
        give_to(MARK);
    }
    say("Cargo scanner resident.\r\n$");
    go_resident(SIGN, 0);
}
