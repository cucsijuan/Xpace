/* tsr.h - what a resident program of the ship needs, in Small-C
 *
 * A program includes this, says what it does on each step in tick(), and
 * ends with go_resident(). It stays on IRQ 5 after the handlers installed
 * before it, on a stack of its own, like the ship's other programs: 'XP' and
 * four more at 103h, the handler it calls at 109h, so they can find each other
 * along the chain. Those bytes are the start-up code's, which has run by then.
 * 10Dh keeps a word of the program's own: the desktop's list of applications.
 *
 * The COMMS screen belongs to whoever has its mark in the screen's table
 * (+9): 0 the desktop, else an application. An application joins the
 * desktop's list with its mark, its name and its icon, is opened when the
 * desktop writes its mark, and goes back by writing 0.
 */
#include <dos.h>

#define IRQ_VECTOR  0x0D
#define IRQ_BIT     0x20
#define DISPLAYS    0x150
#define TYPE_DISPLAYS 6
#define KEY_UP      4
#define KEY_DOWN    5
#define KEY_SELECT  6
#define KEY_BACK    7
#define SIGNATURE   0x103
#define NEXT        0x109
#define LIST        0x10D
#define DESK_SIGN   "XPDK10"
#define MOST_APPS   6
#define APP_SIZE    16
#define IRQ_STACK   768
#define OWN         0x5058              /* 'XP' as a word */

char irq_stack[IRQ_STACK];
unsigned saved_sp;
unsigned saved_ss;
unsigned card_seg;                      /* the display card's table */
unsigned entry;                         /* our screen's entry in it */
unsigned screen_seg;                    /* our screen's memory */
unsigned prev_seg;                      /* the program before the one found */
unsigned desk_seg;
unsigned desk_list;
int my_screen;
int key_count;

#asm
;#def _on_irq
section .text
_on_irq:
    pushf
    call far [cs:109h]
    push ax
    push bx
    push cx
    push dx
    push si
    push di
    push bp
    push ds
    push es
    mov ax, cs
    mov ds, ax
    mov es, ax
    mov [_saved_sp-__dbase], sp
    mov [_saved_ss-__dbase], ss
    mov ss, ax
    mov sp, _irq_stack-__dbase+768
    cld
    call _tick
    mov ss, [_saved_ss-__dbase]
    mov sp, [_saved_sp-__dbase]
    pop es
    pop ds
    pop bp
    pop di
    pop si
    pop dx
    pop cx
    pop bx
    pop ax
    iret
#endasm

/* Where the handler is */
unsigned irq_entry()
{
#asm
    mov ax, _on_irq
#endasm
}

/* Words to a far place, count times */
far_fill(seg, off, word, count)
unsigned seg, off, word, count;
{
#asm
    push es
    push di
    mov es, [bp+4]
    mov di, [bp+6]
    mov ax, [bp+8]
    mov cx, [bp+10]
    rep stosw
    pop di
    pop es
#endasm
}

/* Bytes from one far place to another */
far_copy(to_seg, to, from_seg, from, count)
unsigned to_seg, to, from_seg, from, count;
{
#asm
    push ds
    push es
    push si
    push di
    mov es, [bp+4]
    mov di, [bp+6]
    mov cx, [bp+12]
    mov si, [bp+10]
    mov ds, [bp+8]
    rep movsb
    pop di
    pop si
    pop es
    pop ds
#endasm
}

/* A line of text ending in $ on the console */
say(text)
char *text;
{
#asm
    mov dx, [bp+4]
    mov ah, 9
    int 21h
#endasm
}

/* Gives a program's memory back */
free_program(seg)
unsigned seg;
{
#asm
    push es
    mov es, [bp+4]
    mov ah, 49h
    int 21h
    pop es
#endasm
}

/* Ends, leaving the program in memory, without its environment */
stay()
{
#asm
    mov es, [2Ch]
    mov ah, 49h
    int 21h
    mov dx, __bend+15
    mov cl, 4
    shr dx, cl
    mov ax, 3100h
    int 21h
#endasm
}

/* The resident program with this signature: its segment or 0. prev_seg is the
 * program before it (0: it is first). A program in the way that is not a
 * ship's ends the search. */
unsigned find_resident(sign)
char *sign;
{
    unsigned seg;
    int i;
    prev_seg = 0;
    seg = peek(0, IRQ_VECTOR * 4 + 2);
    while (peek(seg, SIGNATURE) == OWN) {
        i = 0;
        while (i < 6 && peekb(seg, SIGNATURE + i) == sign[i])
            i++;
        if (i == 6)
            return seg;
        prev_seg = seg;
        seg = peek(seg, NEXT + 2);
    }
    return 0;
}

/* Takes the program find_resident found out of the chain, wherever it is */
unlink(seg)
unsigned seg;
{
    disable();
    if (prev_seg) {
        poke(prev_seg, NEXT, peek(seg, NEXT));
        poke(prev_seg, NEXT + 2, peek(seg, NEXT + 2));
    } else {
        poke(0, IRQ_VECTOR * 4, peek(seg, NEXT));
        poke(0, IRQ_VECTOR * 4 + 2, peek(seg, NEXT + 2));
    }
    enable();
}

/* On the chain and resident; does not come back */
go_resident(sign, list)
char *sign;
unsigned list;
{
    unsigned me;
    int i;
    me = getds();
    for (i = 0; i < 6; i++)
        pokeb(me, SIGNATURE + i, sign[i]);
    poke(me, LIST, list);
    disable();
    poke(me, NEXT, peek(0, IRQ_VECTOR * 4));
    poke(me, NEXT + 2, peek(0, IRQ_VECTOR * 4 + 2));
    poke(0, IRQ_VECTOR * 4, irq_entry());
    poke(0, IRQ_VECTOR * 4 + 2, me);
    enable();
    outp(0x21, inp(0x21) & (255 - IRQ_BIT));
    stay();
}

/* Finds the screen of that name (6 characters): 1 if the ship has it */
find_screen(name)
char *name;
{
    int n, i, k;
    unsigned table;
    if (inp(DISPLAYS) != TYPE_DISPLAYS)
        return 0;
    card_seg = inpw(DISPLAYS + 2);
    table = inpw(DISPLAYS + 4);
    n = inp(DISPLAYS + 6);
    for (i = 0; i < n; i++) {
        entry = table + i * 16;
        k = 0;
        while (k < 6 && peekb(card_seg, entry + k) == name[k])
            k++;
        if (k == 6) {
            my_screen = i;
            screen_seg = card_seg + (peek(card_seg, entry + 14) >> 4);
            key_count = inp(DISPLAYS + 8);
            return 1;
        }
    }
    return 0;
}

/* 0 text, 1 graphics */
screen_mode(mode)
int mode;
{
    pokeb(card_seg, entry + 6, mode);
}

/* Whose the screen is */
owner()
{
    return peekb(card_seg, entry + 9);
}

give_to(mark)
int mark;
{
    pokeb(card_seg, entry + 9, mark);
}

/* A key pressed for this screen since the last look, or 0. The screen of
 * interest's keys (zoom in, zoom out, sound) work as up, down, select. */
next_key()
{
    int n, key;
    n = inp(DISPLAYS + 8);
    if (n == key_count)
        return 0;
    key_count = n;
    if (inp(DISPLAYS + 10) != my_screen)
        return 0;
    key = inp(DISPLAYS + 9);
    if (key < KEY_UP)
        key = key + KEY_UP - 1;
    return key;
}

/* An application: on the desktop's list. 1 if there is a desktop to be on */
join_desk(mark, name, icon)
int mark;
char *name, *icon;
{
    int i, k;
    unsigned at;
    desk_seg = find_resident(DESK_SIGN);
    if (desk_seg == 0)
        return 0;
    desk_list = peek(desk_seg, LIST);
    for (i = 0; i < MOST_APPS; i++) {
        at = desk_list + APP_SIZE + i * APP_SIZE;
        if (peekb(desk_seg, at) == 0) {
            for (k = 0; k < 10 && name[k]; k++)
                pokeb(desk_seg, at + 1 + k, name[k]);
            pokeb(desk_seg, at + 1 + k, 0);
            poke(desk_seg, at + 12, icon);
            poke(desk_seg, at + 14, getds());
            pokeb(desk_seg, at, mark);
            pokeb(desk_seg, desk_list, peekb(desk_seg, desk_list) + 1);
            return 1;
        }
    }
    return 0;
}

/* Off the list, and the screen back to the desktop if it had it */
leave_desk(mark)
int mark;
{
    int i;
    unsigned at, seg, list;
    seg = find_resident(DESK_SIGN);
    if (seg == 0)
        return;
    list = peek(seg, LIST);
    for (i = 0; i < MOST_APPS; i++) {
        at = list + APP_SIZE + i * APP_SIZE;
        if (peekb(seg, at) == mark) {
            pokeb(seg, at, 0);
            pokeb(seg, list, peekb(seg, list) + 1);
        }
    }
}
