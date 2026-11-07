/* desk.c - StubCorp DeskPilot 1.0 (DESK.COM)
 *
 *   DESK       stays resident and is the desktop of the COMMS screen
 *   DESK OFF   takes it out of memory, with no application left on it
 *
 * The ship's applications join its list when they start; it shows each one's
 * icon with its name beside it. UP and DOWN move along them and SELECT opens
 * the one chosen, which has the screen until it gives it back. An application
 * is any resident program that keeps to tsr.h: yours too.
 *
 * When it starts it shows its logo and plays its tune on the computer's
 * speaker for a few seconds, then the desktop.
 *
 * It paints a little on every step, so that a whole picture never holds up
 * the ship's other programs: no step does more than one letter.
 *
 * Build: SMALLC DESK.C
 */
#include "tsr.h"
#include "font.h"
#include "logo.h"

#define SCREEN      "COMMS "
#define STRIDE      64                  /* bytes a row: 256 pixels */
#define HEIGHT      192
#define BLACK       0
#define TEAL        1
#define NAVY        2
#define WHITE       3
#define SHOWN       3                   /* applications on a page */
#define FIRST_Y     34
#define ROW_Y       42
#define ICON_X      5                   /* in bytes: 4 pixels each */
#define NAME_X      15
#define BAR_X       14
#define BAR_BYTES   34
#define BAR_Y       5
#define BAR_ROWS    22
#define NAME_Y      8
#define WIN_X       2
#define WIN_BYTES   60
#define WIN_Y       6
#define WIN_ROWS    180
#define BAR_END     27
#define CLEAR_STEPS 4
#define FRAME_STEPS 6
#define SPLASH_STEPS 200                /* steps the logo stays: 60 a second */
#define LOGO_X      24                  /* in bytes */
#define LOGO_Y      26
#define LOGO_BYTES  16
#define LOGO_ROWS   64
#define LOGO_STEPS  4
#define SPEAKER     0x61
#define TIMER       0x42
#define TIMER_MODE  0x43

/* The tune: the timer's divisor (1193182 / hertz, 0 a rest) and steps */
unsigned tune[] = {
    2280, 9,    /* C5 */
    1810, 9,    /* E5 */
    1521, 9,    /* G5 */
    1140, 18,   /* C6 */
    0, 4,
    1355, 9,    /* A5 */
    1140, 9,    /* C6 */
    1015, 9,    /* D6 */
    760, 42,    /* G6 */
    0, 0
};

char apps[APP_SIZE * (MOST_APPS + 1)];
char ink_at[4];
char keep_at[] = { 0x3F, 0xCF, 0xF3, 0xFC };
char label[12];
int shown;                              /* the screen is ours and its picture begun */
int seen;                               /* the list's changes as last drawn */
int cursor;
int count;
int stage;                              /* how far the picture is */
int dirty[SHOWN];                       /* rows to draw again */
int icon_due;
char *text;                             /* letters still to draw */
int text_x;
int text_y;
int text_ink;
int splash;                             /* steps of the logo left */
int started;
int note;                               /* where the tune is */
int note_left;

/* One letter at x, y (pixels), over what is there. The ink goes in by hand:
 * letters of any width start anywhere in a byte. */
far_glyph(seg, off, pos, rows)
unsigned seg, off, pos;
char *rows;
{
#asm
    push es
    push si
    push di
    mov es, [bp+4]
    mov di, [bp+6]
    mov si, [bp+10]
    mov dl, 16
.glyph_row:
    lodsw
    push di
    mov cl, [bp+8]
.glyph_pixel:
    test ax, ax
    jz .glyph_next
    shl ax, 1
    jnc .glyph_on
    mov bl, cl
    xor bh, bh
    mov dh, [es:di]
    and dh, [_keep_at-__dbase+bx]
    or dh, [_ink_at-__dbase+bx]
    mov [es:di], dh
.glyph_on:
    inc cl
    cmp cl, 4
    jne .glyph_pixel
    xor cl, cl
    inc di
    jmp .glyph_pixel
.glyph_next:
    pop di
    add di, 64
    dec dl
    jnz .glyph_row
    pop di
    pop si
    pop es
#endasm
}

/* What the ink puts in each of a byte's four pixels */
colours(ink)
int ink;
{
    ink_at[0] = ink << 6;
    ink_at[1] = ink << 4;
    ink_at[2] = ink << 2;
    ink_at[3] = ink;
}

letter(c, x, y)
int c, x, y;
{
    colours(text_ink);
    far_glyph(screen_seg, y * STRIDE + (x >> 2), x & 3, font + (c - 32) * GLYPH_ROWS);
}

text_width(words)
char *words;
{
    int w;
    w = 0;
    while (*words) {
        w = w + widths[*words - 32];
        words++;
    }
    return w;
}

/* Words from x, y (pixels), a letter a step, on what is painted there */
write_text(words, x, y, ink)
char *words;
int x, y, ink;
{
    text = words;
    text_x = x;
    text_y = y;
    text_ink = ink;
}

centred(words, y, ink)
char *words;
int y, ink;
{
    write_text(words, (256 - text_width(words)) / 2, y, ink);
}

/* A bar of one colour, in bytes across */
bar(x, y, bytes, rows, colour)
int x, y, bytes, rows, colour;
{
    unsigned off, word;
    word = colour * 0x5555;
    off = y * STRIDE + x;
    while (rows) {
        far_fill(screen_seg, off, word, bytes >> 1);
        off = off + STRIDE;
        rows--;
    }
}

/* The n-th application on the list: its place there, or 0 */
unsigned app(n)
int n;
{
    int i;
    unsigned at;
    at = APP_SIZE;
    for (i = 0; i < MOST_APPS; i++) {
        if (apps[at]) {
            if (n == 0)
                return at;
            n--;
        }
        at = at + APP_SIZE;
    }
    return 0;
}

count_apps()
{
    int n;
    n = 0;
    while (app(n))
        n++;
    count = n;
    if (cursor >= count)
        cursor = count ? count - 1 : 0;
}

all_rows()
{
    int i;
    for (i = 0; i < SHOWN; i++)
        dirty[i] = 1;
    icon_due = 1;
}

/* The picture's frame, a part at a time: 1 when it is done */
frame()
{
    int y, end;
    if (stage < CLEAR_STEPS) {
        far_fill(screen_seg, stage * (HEIGHT / CLEAR_STEPS) * STRIDE, TEAL * 0x5555, HEIGHT / CLEAR_STEPS * STRIDE / 2);
    } else if (stage < CLEAR_STEPS + FRAME_STEPS) {
        y = (stage - CLEAR_STEPS) * (WIN_ROWS / FRAME_STEPS);
        end = y + WIN_ROWS / FRAME_STEPS;
        while (y < end) {
            pokeb(screen_seg, (WIN_Y + y) * STRIDE + WIN_X, y < BAR_END - WIN_Y ? 0x2A : 0x3F);
            pokeb(screen_seg, (WIN_Y + y) * STRIDE + WIN_X + WIN_BYTES - 1, y < BAR_END - WIN_Y ? 0xA8 : 0xFC);
            far_fill(screen_seg, (WIN_Y + y) * STRIDE + WIN_X + 1, (y < BAR_END - WIN_Y ? NAVY : WHITE) * 0x5555, (WIN_BYTES - 2) / 2);
            y++;
        }
    } else if (stage == CLEAR_STEPS + FRAME_STEPS) {
        bar(WIN_X, WIN_Y, WIN_BYTES, 1, BLACK);
        bar(WIN_X, BAR_END, WIN_BYTES, 1, BLACK);
        bar(WIN_X, WIN_Y + WIN_ROWS - 1, WIN_BYTES, 1, BLACK);
    } else if (stage == CLEAR_STEPS + FRAME_STEPS + 1) {
        centred("DeskPilot", 9, WHITE);
    } else if (stage == CLEAR_STEPS + FRAME_STEPS + 2) {
        write_text("StubCorp", 236 - text_width("StubCorp"), 164, NAVY);
    } else
        return 1;
    stage++;
    return 0;
}

/* The next note when this one ends */
play()
{
    unsigned divisor;
    if (note_left) {
        note_left--;
        return;
    }
    outp(SPEAKER, inp(SPEAKER) & 0xFC);
    if (tune[note + 1] == 0)
        return;
    divisor = tune[note];
    note_left = tune[note + 1] - 1;
    note = note + 2;
    if (divisor) {
        outp(TIMER_MODE, 0xB6);
        outp(TIMER, divisor & 255);
        outp(TIMER, divisor >> 8);
        outp(SPEAKER, inp(SPEAKER) | 3);
    }
}

/* The logo, a part at a time */
logo_screen()
{
    int r, end;
    unsigned off, from;
    if (stage < CLEAR_STEPS) {
        far_fill(screen_seg, stage * (HEIGHT / CLEAR_STEPS) * STRIDE, NAVY * 0x5555, HEIGHT / CLEAR_STEPS * STRIDE / 2);
    } else if (stage < CLEAR_STEPS + LOGO_STEPS) {
        r = (stage - CLEAR_STEPS) * (LOGO_ROWS / LOGO_STEPS);
        end = r + LOGO_ROWS / LOGO_STEPS;
        off = (LOGO_Y + r) * STRIDE + LOGO_X;
        from = logo + r * LOGO_BYTES;
        while (r < end) {
            far_copy(screen_seg, off, getds(), from, LOGO_BYTES);
            off = off + STRIDE;
            from = from + LOGO_BYTES;
            r++;
        }
    } else if (stage == CLEAR_STEPS + LOGO_STEPS) {
        centred("DeskPilot", 104, WHITE);
    } else if (stage == CLEAR_STEPS + LOGO_STEPS + 1) {
        centred("StubCorp", 128, TEAL);
    } else
        return;
    stage++;
}

/* A row of the list, its name left to write */
draw_row(row)
int row;
{
    int n, y, r, chosen;
    unsigned at, off, icon, seg;
    n = cursor / SHOWN * SHOWN + row;
    y = FIRST_Y + row * ROW_Y;
    at = n < count ? app(n) : 0;
    chosen = at && n == cursor;
    bar(BAR_X, y + BAR_Y, BAR_BYTES, BAR_ROWS, chosen ? NAVY : WHITE);
    if (icon_due) {
        off = y * STRIDE + ICON_X;
        if (at) {
            icon = apps[at + 12] & 255 | (apps[at + 13] << 8);
            seg = apps[at + 14] & 255 | (apps[at + 15] << 8);
            for (r = 0; r < 32; r++) {
                far_copy(screen_seg, off, seg, icon, 8);
                off = off + STRIDE;
                icon = icon + 8;
            }
        } else
            bar(ICON_X, y, 8, 32, WHITE);
    }
    if (at) {
        for (r = 0; r < 10 && apps[at + 1 + r]; r++)
            label[r] = apps[at + 1 + r];
        label[r] = 0;
        write_text(label, NAME_X * 4, y + NAME_Y, chosen ? WHITE : BLACK);
    }
}

/* One piece of the picture */
paint()
{
    int i;
    if (text && *text) {
        letter(*text, text_x, text_y);
        text_x = text_x + widths[*text - 32];
        text++;
        return;
    }
    if (splash > 0) {
        logo_screen();
        return;
    }
    if (!frame())
        return;
    for (i = 0; i < SHOWN; i++)
        if (dirty[i]) {
            dirty[i] = 0;
            draw_row(i);
            return;
        }
    icon_due = 0;
}

move(to)
int to;
{
    int page;
    page = cursor / SHOWN;
    dirty[cursor % SHOWN] = 1;
    cursor = to;
    dirty[cursor % SHOWN] = 1;
    if (cursor / SHOWN != page)
        all_rows();
}

press(key)
int key;
{
    unsigned at;
    if (key == KEY_UP && cursor > 0)
        move(cursor - 1);
    else if (key == KEY_DOWN && cursor + 1 < count)
        move(cursor + 1);
    else if (key == KEY_SELECT && count) {
        at = app(cursor);
        give_to(apps[at]);
    }
}

tick()
{
    int key;
    key = next_key();
    if (owner()) {
        shown = 0;
        return;
    }
    if (splash == 0 && !started) {
        started = 1;
        splash = SPLASH_STEPS;
        screen_mode(1);
        stage = 0;
        text = 0;
        return;
    }
    if (splash > 0) {
        play();
        paint();
        splash--;
        if (splash == 0)
            outp(SPEAKER, inp(SPEAKER) & 0xFC);
        return;
    }
    if (!shown) {
        shown = 1;
        screen_mode(1);
        stage = 0;
        text = 0;
        seen = apps[0];
        count_apps();
        all_rows();
        return;
    }
    if (seen != apps[0]) {
        seen = apps[0];
        count_apps();
        all_rows();
    }
    if (key)
        press(key);
    paint();
}

main(argc, argv)
int argc;
char **argv;
{
    unsigned seg;
    if (argc > 1 && (argv[1][0] == 'O' || argv[1][0] == 'o')) {
        seg = find_resident(DESK_SIGN);
        if (seg == 0) {
            say("DeskPilot is not resident.\r\n$");
            return 1;
        }
        if (busy(seg)) {
            say("DeskPilot: applications are still running on it.\r\n$");
            return 1;
        }
        unlink(seg);
        free_program(seg);
        say("DeskPilot removed.\r\n$");
        return 0;
    }
    if (find_resident(DESK_SIGN)) {
        say("DeskPilot is already resident.\r\n$");
        return 1;
    }
    if (!find_screen(SCREEN) || peek(card_seg, entry + 10) < 256 || peek(card_seg, entry + 12) < HEIGHT) {
        say("DESK: the ship has no COMMS screen (256 x 192).\r\n$");
        return 1;
    }
    give_to(0);
    say("StubCorp DeskPilot 1.0 resident on the COMMS screen.\r\n$");
    go_resident(DESK_SIGN, apps);
}

/* Any application on the resident copy's list */
busy(seg)
unsigned seg;
{
    int i;
    unsigned at;
    at = peek(seg, LIST);
    for (i = 1; i <= MOST_APPS; i++)
        if (peekb(seg, at + i * APP_SIZE))
            return 1;
    return 0;
}
