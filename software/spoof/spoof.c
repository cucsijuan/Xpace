/* spoof.c - SPOOF 1.0, transponder override (SPOOF.COM)
 *
 *   SPOOF         asks for a callsign and has the ship's radio send it
 *                 in place of the ship's own; an empty line, the ship's own
 *
 * The radio (16 ports at 200h): +0 type 18  +1 version (4 has a
 * transponder)  +4 status (bit 6 sending another callsign)  +5 write: 6 the
 * transponder, with +6 = 1 send the 8 characters at D000:1D00, 0 the ship's
 * own.
 *
 * Build: SMALLC SPOOF.C
 */
#include <conio.h>
#include <dos.h>

#define RADIO       0x200
#define TYPE_RADIO  18
#define TRANSPONDER 6
#define SENDING     64
#define SEG         0xD000
#define CALLSIGN    0x1D00
#define VIDEO       0xB800
#define COLS        80
#define DIM         0x02
#define BRIGHT      0x0A
#define WHITE       0x0F
#define ALARM       0x0C
#define INVERSE     0x20

int line;                               /* the next row the log writes on */

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

/* A row of one character */
rule(r, c, attr)
int r, c, attr;
{
    int col;
    for (col = 0; col < COLS; col++)
        poke(VIDEO, (r * COLS + col) * 2, (attr << 8) | c);
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

/* A line of the log, typed out a character at a time */
say(words, attr)
char *words;
int attr;
{
    int col;
    col = at(line, 2, "> ", DIM);
    while (*words) {
        poke(VIDEO, (line * COLS + col) * 2, (attr << 8) | (*words & 255));
        words++;
        col++;
    }
    line++;
}

/* Dots out to a column, a few at a time, then the word */
dots(r, from, to, word, attr)
int r, from, to, attr;
char *word;
{
    while (from < to) {
        poke(VIDEO, (r * COLS + from) * 2, (DIM << 8) | '.');
        from++;
        wait(1);
    }
    at(r, to + 1, word, attr);
}

/* A bar filling up across the row */
bar(r)
int r;
{
    int i;
    at(r, 2, "[", DIM);
    at(r, 43, "]", DIM);
    for (i = 0; i < 40; i++) {
        poke(VIDEO, (r * COLS + 3 + i) * 2, (BRIGHT << 8) | 219);
        wait(1);
    }
}

/* A row of the banner: '#' is a solid block */
art(r, col, pattern)
int r, col;
char *pattern;
{
    while (*pattern) {
        if (*pattern == '#')
            poke(VIDEO, (r * COLS + col) * 2, (BRIGHT << 8) | 219);
        pattern++;
        col++;
    }
}

banner()
{
    art(1, 4, "#### #### #### #### ####");
    art(2, 4, "#    #  # #  # #  # #   ");
    art(3, 4, "#### #### #  # #  # ### ");
    art(4, 4, "   # #    #  # #  # #   ");
    art(5, 4, "#### #    #### #### #   ");
    at(2, 32, "transponder override 1.0", WHITE);
    at(3, 32, "no warranty. no questions. no names.", DIM);
    rule(7, 196, DIM);
}

/* Reads a callsign: up to 8 characters, upper case; the count */
ask(r, col, sign)
int r, col;
char *sign;
{
    int n, c;
    n = 0;
    for (;;) {
        poke(VIDEO, (r * COLS + col + n) * 2, (INVERSE << 8) | ' ');
        c = getch();
        if (c == 13)
            break;
        if (c == 27)
            return -1;
        if (c == 8 && n > 0) {
            poke(VIDEO, (r * COLS + col + n) * 2, (DIM << 8) | ' ');
            n--;
            continue;
        }
        if (c >= 'a' && c <= 'z')
            c = c - 32;
        if (n < 8 && (c == '-' || (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z'))) {
            poke(VIDEO, (r * COLS + col + n) * 2, (WHITE << 8) | c);
            sign[n] = c;
            n++;
        }
    }
    poke(VIDEO, (r * COLS + col + n) * 2, (DIM << 8) | ' ');
    return n;
}

main()
{
    char sign[9];
    int n, i;
    for (i = 0; i < 25; i++)
        rule(i, ' ', DIM);
    banner();
    line = 9;
    say("probing ship bus", DIM);
    dots(line - 1, 21, 40, inp(RADIO) == TYPE_RADIO ? "RADIO" : "NOTHING", inp(RADIO) == TYPE_RADIO ? BRIGHT : ALARM);
    if (inp(RADIO) != TYPE_RADIO || inp(RADIO + 1) < 4) {
        say("no transponder this radio will let us at.", ALARM);
        return 1;
    }
    say("transponder", DIM);
    dots(line - 1, 16, 40, "OPEN", BRIGHT);
    say((inp(RADIO + 4) & SENDING) ? "radio sends another callsign now" : "radio sends the ship's own callsign", WHITE);
    line++;
    at(line, 2, "callsign to send (empty: the ship's own) ", BRIGHT);
    n = ask(line, 43, sign);
    line = line + 2;
    if (n < 0) {
        say("left as it was.", DIM);
        return 0;
    }
    for (i = 0; i < 8; i++)
        pokeb(SEG, CALLSIGN + i, i < n ? sign[i] : ' ');
    say("writing transponder", DIM);
    bar(line);
    line = line + 2;
    outp(RADIO + 6, n > 0 ? 1 : 0);
    outp(RADIO + 5, TRANSPONDER);
    wait(2);
    if (n > 0 && (inp(RADIO + 4) & SENDING)) {
        i = at(line, 2, "> NOW SENDING ", BRIGHT);
        sign[n] = 0;
        at(line, i, sign, WHITE);
    }
    else if (n == 0)
        at(line, 2, "> back to the ship's own callsign", BRIGHT);
    else
        at(line, 2, "> the radio did not take it", ALARM);
    line = line + 2;
    at(line, 2, "press any key", DIM);
    getch();
    for (i = 0; i < 25; i++)
        rule(i, ' ', 0x07);
    return 0;
}
