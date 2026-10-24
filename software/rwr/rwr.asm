; rwr.asm - CucsiCorp Threat Warning 1.1 (RWR.COM)
;
;   RWR         stays resident and shows the radar warning receiver
;   RWR OFF     takes it out of memory
;
; The warning receiver hears the radars whose beams light the ship. On the
; RWR screen (graphics) the ship sits in the middle, nose up, and every
; emitter is drawn at its bearing, as far out as the receiver guesses it
; to be: a ring every 2 km, 8 km at the edge (anything further sits just
; inside the edge, anything very close just off the ship). The guess comes
; from the strength of the signal, so it is only roughly right.
;
; The symbol is the emitter's type letter (D drone, F fighter, U unknown)
; in a circle while it is only searching, in a diamond while it has the
; ship locked. A missile coming (the receiver's plume warner sees its
; motor) is an M in a diamond that blinks.
;
; Sixty times a second, on IRQ 5 (after the handlers installed before it),
; it looks at one line of the receiver's table: each symbol about four
; times a second. A symbol that has not changed is left alone; one that
; has is rubbed out and drawn again. The symbols are XORed onto the rings,
; so drawing one a second time takes it off and leaves the rings as they
; were.
;
; The devices (16 ports each, words little-endian):
;
;   150h  display card       +2 segment  +4 screen table  +6 screens
;           a screen (16 bytes): +0 name  +6 mode (0 text, 1 graphics)
;                            +7 columns  +8 rows  +10 width  +12 height
;                            +14 memory (offset)
;   170h  warning receiver   +0 type 8  +2 emitters in the table
;                            +6 segment  +8 table (offset)
;           an emitter (8 bytes): +0 id  +2 bearing (65536 to the turn,
;                            right positive)  +4 range (m)  +6 type letter
;                            +7 flags (bit 0 locked on us, bit 3 a missile)

org 100h

DISPLAYS    equ 150h
RWR         equ 170h
TYPE_DISPLAYS equ 6
TYPE_RWR    equ 8
IRQ_VECTOR  equ 0Dh
IRQ_BIT     equ 20h

SLOTS       equ 16                  ; lines in the receiver's table
EDGE_RANGE  equ 8000                ; metres at the edge
RINGS       equ 4                   ; a ring every EDGE_RANGE / RINGS
SYMBOL_HALF equ 6                   ; the symbols are 13 x 13
TICKS       equ 12                  ; marks round the edge, every 30 degrees
TICK_TURN   equ 5461                ; 65536 / TICKS
BRIGHT      equ 3
HALF        equ 2
DIM         equ 1

    jmp start

; ======================================================================
; The resident part
; ======================================================================

signature   db 'XPRW10'             ; 103h
old_irq     dw 0, 0                 ; 109h

on_irq:
    pushf
    call far [cs:old_irq]
    push ax
    push bx
    push cx
    push dx
    push si
    push di
    push bp
    push ds
    push es
    push cs
    pop ds
    push cs
    pop es
    cld
    call step
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

; One line of the table: what it should show; if that is not what is
; drawn there, the old symbol off and the new one on
step:
    inc byte [ticks]
    mov bl, [slot]
    mov al, bl
    inc al
    and al, SLOTS - 1
    mov [slot], al
    xor bh, bh
    mov di, bx                      ; DI = its line in 'drawn' (6 bytes)
    shl di, 1
    add di, bx
    shl di, 1
    add di, drawn

    mov word [want_kind], 0
    mov dx, RWR + 2
    in al, dx
    cmp bl, al
    jae .compare                    ; past the last emitter: nothing
    mov si, bx
    mov cl, 3
    shl si, cl
    add si, [table_off]
    push ds
    mov ds, [cs:table_seg]
    mov ax, [si + 2]                ; bearing
    mov cx, [si + 4]                ; range
    mov dx, [si + 6]                ; DL letter, DH flags
    pop ds
    mov [bearing], ax
    mov [range], cx
    test dh, 8
    jz .radar
    test byte [ticks], SLOTS        ; a missile blinks: away every other look
    jnz .compare
    mov dh, 2
    jmp .kind
.radar:
    and dh, 1
    inc dh                          ; 1 searching, 2 locked
.kind:
    xchg dl, dh
    mov [want_kind], dx             ; kind, letter
    call place

.compare:
    mov ax, [want_kind]
    cmp ax, [di + 4]
    jne .redraw
    test al, al
    jz .done                        ; nothing wanted, nothing there
    mov ax, [want_x]
    cmp ax, [di]
    jne .redraw
    mov ax, [want_y]
    cmp ax, [di + 2]
    je .done
.redraw:
    mov es, [screen_seg]
    push di
    mov cx, [di]                    ; the old one off
    mov dx, [di + 2]
    mov ax, [di + 4]
    call xor_symbol
    pop di
    mov cx, [want_x]                ; the new one on
    mov dx, [want_y]
    mov ax, [want_kind]
    mov [di], cx
    mov [di + 2], dx
    mov [di + 4], ax
    call xor_symbol
.done:
    ret

; bearing, range -> want_x, want_y: out from the middle as far as the
; range, kept between just off the ship and just inside the edge
place:
    mov ax, [range]
    cmp ax, EDGE_RANGE
    jb .inside
    mov ax, EDGE_RANGE
.inside:
    mul word [radius]
    mov cx, EDGE_RANGE
    div cx                          ; AX = pixels out
    mov cx, [radius]
    sub cx, SYMBOL_HALF + 1
    cmp ax, cx
    jle .not_out
    mov ax, cx
.not_out:
    cmp ax, SYMBOL_HALF + 3
    jge .not_in
    mov ax, SYMBOL_HALF + 3
.not_in:
    mov [out_px], ax
    mov ax, [bearing]
    call sine
    imul word [out_px]              ; at most 256 times the radius: a word
    mov cl, 8
    sar ax, cl
    add ax, [centre_x]
    mov [want_x], ax
    mov ax, [bearing]
    add ax, 16384
    call sine                       ; the cosine
    imul word [out_px]
    mov cl, 8
    sar ax, cl
    mov dx, [centre_y]
    sub dx, ax
    mov [want_y], dx
    ret

; AX a binary angle: AX = its sine times 256 (from the quarter-wave table,
; 256 steps to the turn). Changes BX.
sine:
    mov bl, ah
    mov al, ah
    xor bh, bh
    and bl, 3Fh
    test al, 40h
    jz .rising
    neg bx                          ; second and fourth quarters: falling
    add bx, 64
.rising:
    shl bx, 1
    mov bx, [sines + bx]
    test al, 80h
    jz .positive
    neg bx                          ; the second half turn: below zero
.positive:
    mov ax, bx
    ret

; AL kind (0 nothing, 1 circle, 2 diamond), AH letter: XORs the symbol
; centred on (CX, DX). A whole row at a time: the shapes are kept ready in
; bright 2-bit pixels, so a row is a shift and four XORs. 'place' keeps the
; symbol on the screen, so nothing is clipped.
xor_symbol:
    test al, al
    jnz .draw
    ret
.draw:
    push ax
    mov si, circle
    cmp al, 1
    je .shape
    mov si, diamond
.shape:
    sub cx, SYMBOL_HALF
    sub dx, SYMBOL_HALF
    push cx
    push dx
    call address
    mov bp, 2 * SYMBOL_HALF + 1
.row:
    mov dx, [si]                    ; 16 pixels: DX the left 8, AX the right 8
    mov ax, [si + 2]
    mov cl, bl
    xor ch, ch
    jcxz .put
.shift:
    shr dx, 1
    rcr ax, 1
    loop .shift
.put:
    xor [es:di], dh
    xor [es:di + 1], dl
    xor [es:di + 2], ah
    xor [es:di + 3], al
    add si, 4
    add di, [pitch]
    dec bp
    jnz .row
    pop dx
    pop cx
    pop ax
    mov al, ah                      ; the letter, 5 x 5 in the middle
    sub al, 'A'
    cmp al, 25
    ja .done
    mov bl, 10
    mul bl
    mov si, ax
    add si, font
    add cx, SYMBOL_HALF - 2
    add dx, SYMBOL_HALF - 2
    call address
    mov bp, 5
.letter:
    mov ax, [si]
    mov cl, bl
    xor ch, ch
    jcxz .put_letter
.shift_letter:
    shr ax, 1
    loop .shift_letter
.put_letter:
    xor [es:di], ah
    xor [es:di + 1], al
    add si, 2
    add di, [pitch]
    dec bp
    jnz .letter
.done:
    ret

; DI = the offset of the byte holding pixel (CX, DX), BL = how many bits
; into it the pixel starts. Keeps CX and DX.
address:
    push dx
    mov ax, dx
    mul word [pitch]
    pop dx
    mov di, cx
    shr di, 1
    shr di, 1
    add di, ax
    mov bl, cl
    and bl, 3
    shl bl, 1
    ret

sines       dw 0, 6, 13, 19, 25, 31, 38, 44, 50, 56, 62, 68, 74, 80, 86, 92, 98
            dw 104, 109, 115, 121, 126, 132, 137, 142, 147, 152, 157, 162, 167, 172, 177, 181
            dw 185, 190, 194, 198, 202, 206, 209, 213, 216, 220, 223, 226, 229, 231, 234, 237
            dw 239, 241, 243, 245, 247, 248, 250, 251, 252, 253, 254, 255, 255, 256, 256, 256

; 13 x 13 in bright 2-bit pixels, a pair of words a row (the leftmost pixel
; in the top bits of the first)
circle      dw 000FFh, 0C000h, 00F00h, 03C00h, 03C00h, 00F00h, 03000h, 00300h
            dw 0C000h, 000C0h, 0C000h, 000C0h, 0C000h, 000C0h, 0C000h, 000C0h
            dw 0C000h, 000C0h, 03000h, 00300h, 03C00h, 00F00h, 00F00h, 03C00h
            dw 000FFh, 0C000h
diamond     dw 0000Ch, 00000h, 00033h, 00000h, 000C0h, 0C000h, 00300h, 03000h
            dw 00C00h, 00C00h, 03000h, 00300h, 0C000h, 000C0h, 03000h, 00300h
            dw 00C00h, 00C00h, 00300h, 03000h, 000C0h, 0C000h, 00033h, 00000h
            dw 0000Ch, 00000h

; A to Z, 5 x 5 in bright 2-bit pixels, a word a row
font        dw 03F00h, 0C0C0h, 0FFC0h, 0C0C0h, 0C0C0h    ; A
            dw 0FF00h, 0C0C0h, 0FF00h, 0C0C0h, 0FF00h    ; B
            dw 03FC0h, 0C000h, 0C000h, 0C000h, 03FC0h    ; C
            dw 0FF00h, 0C0C0h, 0C0C0h, 0C0C0h, 0FF00h    ; D
            dw 0FFC0h, 0C000h, 0FF00h, 0C000h, 0FFC0h    ; E
            dw 0FFC0h, 0C000h, 0FF00h, 0C000h, 0C000h    ; F
            dw 03FC0h, 0C000h, 0C3C0h, 0C0C0h, 03FC0h    ; G
            dw 0C0C0h, 0C0C0h, 0FFC0h, 0C0C0h, 0C0C0h    ; H
            dw 0FFC0h, 00C00h, 00C00h, 00C00h, 0FFC0h    ; I
            dw 00FC0h, 000C0h, 000C0h, 0C0C0h, 03F00h    ; J
            dw 0C0C0h, 0C300h, 0FC00h, 0C300h, 0C0C0h    ; K
            dw 0C000h, 0C000h, 0C000h, 0C000h, 0FFC0h    ; L
            dw 0C0C0h, 0F3C0h, 0CCC0h, 0C0C0h, 0C0C0h    ; M
            dw 0C0C0h, 0F0C0h, 0CCC0h, 0C3C0h, 0C0C0h    ; N
            dw 03F00h, 0C0C0h, 0C0C0h, 0C0C0h, 03F00h    ; O
            dw 0FF00h, 0C0C0h, 0FF00h, 0C000h, 0C000h    ; P
            dw 03F00h, 0C0C0h, 0CCC0h, 0C300h, 03CC0h    ; Q
            dw 0FF00h, 0C0C0h, 0FF00h, 0C300h, 0C0C0h    ; R
            dw 03FC0h, 0C000h, 03F00h, 000C0h, 0FF00h    ; S
            dw 0FFC0h, 00C00h, 00C00h, 00C00h, 00C00h    ; T
            dw 0C0C0h, 0C0C0h, 0C0C0h, 0C0C0h, 03F00h    ; U
            dw 0C0C0h, 0C0C0h, 0C0C0h, 03300h, 00C00h    ; V
            dw 0C0C0h, 0C0C0h, 0CCC0h, 0F3C0h, 0C0C0h    ; W
            dw 0C0C0h, 03300h, 00C00h, 03300h, 0C0C0h    ; X
            dw 0C0C0h, 03300h, 00C00h, 00C00h, 00C00h    ; Y
            dw 0FFC0h, 00300h, 00C00h, 03000h, 0FFC0h    ; Z

slot        db 0
ticks       db 0
table_seg   dw 0
table_off   dw 0
screen_seg  dw 0
screen_words dw 0
scr_w       dw 0
scr_h       dw 0
pitch       dw 0                    ; bytes a row
centre_x    dw 0
centre_y    dw 0
radius      dw 0
bearing     dw 0
range       dw 0
out_px      dw 0
want_x      dw 0
want_y      dw 0
want_kind   dw 0                    ; kind, letter
drawn       times SLOTS * 3 dw 0    ; per line: x, y, kind and letter

resident_end:

; ======================================================================
; Installing and removing (not kept in memory)
; ======================================================================

start:
    mov si, 81h
.blank:
    lodsb
    cmp al, ' '
    je .blank
    and al, 0DFh
    cmp al, 'O'
    jne install
    jmp remove

install:
    call find_resident
    jne .not_yet
    mov dx, msg_already
    jmp quit_message
.not_yet:
    mov dx, RWR
    mov bl, TYPE_RWR
    call check_device
    mov dx, DISPLAYS
    mov bl, TYPE_DISPLAYS
    call check_device
    mov dx, RWR + 6                 ; the receiver's table
    in ax, dx
    mov [table_seg], ax
    mov dx, RWR + 8
    in ax, dx
    mov [table_off], ax

    mov si, n_rwr
    call find_screen
    jc .no_screen
    cmp word [es:bx + 10], 32
    jb .no_screen
    cmp word [es:bx + 12], 32
    jae .screen
.no_screen:
    mov dx, msg_no_screen
    jmp quit_message
.screen:
    mov byte [es:bx + 6], 1         ; graphics
    mov ax, [es:bx + 10]
    mov [scr_w], ax
    shr ax, 1
    mov [centre_x], ax
    shr ax, 1
    mov [pitch], ax
    mov cx, [es:bx + 12]
    mov [scr_h], cx
    mul cx
    inc ax
    shr ax, 1
    mov [screen_words], ax
    mov ax, [scr_h]
    shr ax, 1
    mov [centre_y], ax
    cmp ax, [centre_x]              ; the radius: the smaller half, less one
    jbe .radius
    mov ax, [centre_x]
.radius:
    dec ax
    mov [radius], ax
    call screen_segment
    mov [screen_seg], ax
    call draw_rings

    mov ax, 3500h + IRQ_VECTOR
    int 21h
    mov [old_irq], bx
    mov [old_irq + 2], es
    mov dx, on_irq
    mov ax, 2500h + IRQ_VECTOR
    int 21h
    in al, 21h
    and al, 0FFh - IRQ_BIT
    out 21h, al

    mov es, [2Ch]
    mov ah, 49h
    int 21h
    mov dx, msg_installed
    mov ah, 09h
    int 21h
    mov dx, (resident_end - $$ + 100h + 15) / 16
    mov ax, 3100h
    int 21h

remove:
    call find_resident
    je .found
    mov dx, msg_not_running
    jmp quit_message
.found:
    push ds
    lds dx, [es:old_irq]
    mov ax, 2500h + IRQ_VECTOR
    int 21h
    pop ds
    mov ax, [es:screen_seg]         ; the resident copy's screen goes dark
    mov cx, [es:screen_words]
    push es
    mov es, ax
    xor di, di
    xor ax, ax
    rep stosw
    pop es
    mov ah, 49h
    int 21h
    mov dx, msg_removed
    jmp quit_message

find_resident:
    mov ax, 3500h + IRQ_VECTOR
    int 21h
    cmp bx, on_irq
    jne .no
    mov si, signature
    mov di, si
    mov cx, 6
    repe cmpsb
    ret
.no:
    or al, 1
    ret

check_device:
    in al, dx
    cmp al, bl
    jne .missing
    ret
.missing:
    mov dx, msg_no_device
quit_message:
    mov ah, 09h
    int 21h
    mov ax, 4C01h
    int 21h

; DS:SI a screen's name (6 characters): CF clear and ES:BX its entry in
; the display card's screen table, or CF set if the ship has no such screen
find_screen:
    mov dx, DISPLAYS + 2
    in ax, dx
    mov es, ax
    mov dx, DISPLAYS + 4
    in ax, dx
    mov bx, ax
    mov dx, DISPLAYS + 6
    in al, dx
    xor ah, ah
    mov cx, ax
.next:
    jcxz .missing
    dec cx
    push cx
    push si
    mov di, bx
    mov cx, 6
    repe cmpsb
    pop si
    pop cx
    je .found
    add bx, 16
    jmp .next
.missing:
    stc
    ret
.found:
    clc
    ret

; ES:BX a screen's entry: AX = the segment of its memory
screen_segment:
    mov ax, [es:bx + 14]
    mov cl, 4
    shr ax, cl
    mov cx, es
    add ax, cx
    ret

; What stays drawn: the edge, the range rings (dotted), marks every 30
; degrees inside the edge (longer at the nose) and the ship in the middle
draw_rings:
    push es
    mov es, [screen_seg]
    xor di, di
    mov cx, [screen_words]
    xor ax, ax
    rep stosw
    mov bl, HALF                    ; the edge
    mov bp, 1
    mov ax, [radius]
    call ring
    mov word [ring_n], 1            ; the ranges
.range:
    mov ax, [radius]
    mul word [ring_n]
    mov cx, RINGS
    div cx
    mov bl, DIM
    mov bp, 3
    call ring
    inc word [ring_n]
    cmp word [ring_n], RINGS
    jb .range

    mov word [tick_angle], 0        ; the marks
    mov word [tick_n], TICKS
.tick:
    mov ax, [radius]
    sub ax, 4
    cmp word [tick_angle], 0
    jne .short
    sub ax, 4
.short:
    mov [tick_r], ax
.tick_pixel:
    mov ax, [tick_angle]
    call sine
    imul word [tick_r]
    mov cl, 8
    sar ax, cl
    add ax, [centre_x]
    mov [tick_x], ax
    mov ax, [tick_angle]
    add ax, 16384
    call sine
    imul word [tick_r]
    mov cl, 8
    sar ax, cl
    mov dx, [centre_y]
    sub dx, ax
    mov cx, [tick_x]
    mov bl, HALF
    call plot
    inc word [tick_r]
    mov ax, [tick_r]
    cmp ax, [radius]
    jl .tick_pixel
    add word [tick_angle], TICK_TURN
    dec word [tick_n]
    jnz .tick

    mov si, ship                    ; the ship
    mov bl, HALF
.ship:
    lodsw
    cmp ax, 8000h
    je .done
    mov cx, [centre_x]
    add cx, ax
    lodsw
    mov dx, [centre_y]
    add dx, ax
    call plot
    jmp .ship
.done:
    pop es
    ret

; A circle of radius AX round the middle, every BP-th point, value BL
; (the midpoint way: one octant worked out, drawn eight times)
ring:
    mov [ring_x], ax
    mov word [ring_y], 0
    mov cx, 1
    sub cx, ax
    mov [ring_err], cx
    xor si, si
.next:
    mov ax, [ring_x]
    cmp ax, [ring_y]
    jl .done
    inc si
    cmp si, bp
    jb .skip
    xor si, si
    mov ax, [ring_x]
    mov di, [ring_y]
    call plot4
    mov ax, [ring_y]
    mov di, [ring_x]
    call plot4
.skip:
    inc word [ring_y]
    mov ax, [ring_y]
    cmp word [ring_err], 0
    jge .x_in
    shl ax, 1
    inc ax
    add [ring_err], ax
    jmp .next
.x_in:
    dec word [ring_x]
    sub ax, [ring_x]
    shl ax, 1
    inc ax
    add [ring_err], ax
    jmp .next
.done:
    ret

; The four points (+-AX, +-DI) round the middle, value BL
plot4:
    mov cx, [centre_x]
    add cx, ax
    mov dx, [centre_y]
    add dx, di
    call plot
    mov dx, [centre_y]
    sub dx, di
    call plot
    mov cx, [centre_x]
    sub cx, ax
    call plot
    mov dx, [centre_y]
    add dx, di
    call plot
    ret

; OR value BL into the pixel at (CX, DX), if that is on the screen
plot:
    cmp cx, [scr_w]
    jae .off
    cmp dx, [scr_h]
    jae .off
    push ax
    push cx
    push dx
    push di
    mov ax, dx
    mul word [pitch]
    mov di, cx
    shr di, 1
    shr di, 1
    add di, ax
    and cl, 3
    mov ch, 3
    sub ch, cl
    mov cl, ch
    shl cl, 1
    mov al, bl
    shl al, cl
    or [es:di], al
    pop di
    pop dx
    pop cx
    pop ax
.off:
    ret

; The ship from above, nose up: x, y pairs from the middle
ship        dw 0, -3, 0, -2, 0, -1, 0, 0, 0, 1, 0, 2
            dw -1, 0, -2, 1, -3, 1, 1, 0, 2, 1, 3, 1
            dw -1, 3, 1, 3
            dw 8000h

ring_n      dw 0
ring_x      dw 0
ring_y      dw 0
ring_err    dw 0
tick_angle  dw 0
tick_n      dw 0
tick_r      dw 0
tick_x      dw 0

n_rwr           db 'RWR   '

msg_installed   db 'Threat warning resident: on the RWR screen.', 13, 10
                db 'RWR OFF removes it.', 13, 10, '$'
msg_already     db 'Threat warning is already resident.', 13, 10, '$'
msg_not_running db 'Threat warning is not resident, or another program took', 13, 10
                db 'IRQ 5 after it: remove that one first.', 13, 10, '$'
msg_removed     db 'Threat warning removed.', 13, 10, '$'
msg_no_device   db 'RWR: the warning receiver or the display card is not', 13, 10
                db 'on the bus.', 13, 10, '$'
msg_no_screen   db 'RWR: the ship has no RWR screen (32 x 32 at least).', 13, 10, '$'
