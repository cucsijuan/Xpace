; rwr.asm - CucsiCorp Threat Warning 1.5 (RWR.COM)
;
;   RWR         stays resident and shows the radar warning receiver;
;               works the countermeasures dispenser and the jammer
;   RWR OFF     takes it out of memory
;
; The warning receiver hears the radars whose beams light the ship. On the
; RWR screen (graphics) the ship sits in the middle, nose up, and every
; emitter is drawn at its bearing, as far out as the receiver guesses it
; to be: the edge is 8, 6, 4, 2 or 1 km away (written in the top left
; corner), with a ring every quarter of that; anything further sits just
; inside the edge, anything very close just off the ship. The guess comes
; from the strength of the signal, so it is only roughly right.
;
; With the RWR screen picked as the screen of interest, its keys work:
; zoom in and out step the range, sound switches the receiver's tones off
; and on (MUTE in the bottom left corner while off).
;
; If the ship has a countermeasures dispenser, the pilot's chaff and flare
; buttons throw one of each on every press, and the bottom right corner
; shows how many are left: C chaff, F flares.
;
; If the ship has a jammer, the pilot's jammer button steps it on each
; press: off, noise, deception (if the model can deceive), off; the top
; right corner shows JAM N or JAM D while it sends. Eight times a second
; it aims the jammer's beam at the radar that has the ship locked (one
; guiding a missile first), at its bearing on the receiver; with none, the
; jammer sends all round.
;
; The symbol is the emitter's type letter (D drone, F fighter, U unknown)
; in a circle while it is only searching, in a diamond while it has the
; ship locked, a diamond that blinks while it guides a missile at the ship
; (a launch). A missile coming (the receiver's plume warner sees its
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
;   100h  pilot's controls   +14 buttons: bit 6 chaff, 7 flare
;                            +15 buttons: bit 6 jammer
;   150h  display card       +2 segment  +4 screen table  +6 screens
;                            +8 keys pressed so far  +9 the last key (1 zoom
;                            in, 2 zoom out, 3 sound)  +10 its screen
;           a screen (16 bytes): +0 name  +6 mode (0 text, 1 graphics)
;                            +7 columns  +8 rows  +10 width  +12 height
;                            +14 memory (offset)
;   170h  warning receiver   +0 type 8  +2 emitters in the table
;                            +5 audio (bit 0 off)  +6 segment  +8 table
;           an emitter (8 bytes): +0 id  +2 bearing (65536 to the turn,
;                            right positive)  +4 range (m)  +6 type letter
;                            +7 flags (bit 0 locked on us, bit 3 a missile,
;                            bit 4 guiding a missile at us)
;   180h  dispenser          +0 type 10  +2 chaff left  +3 flares left
;                            +4 write: bit 0 throw chaff, bit 1 a flare
;   1E0h  jammer             +0 type 16  +2 mode (0 off, 1 noise,
;                            2 deception)  +3 can do (bit 1 deception)
;                            +4 azimuth  +6 elevation  +8 write: 0 all
;                            round, 1 aimed at +4/+6

org 100h

CTRL        equ 100h
DISPLAYS    equ 150h
RWR         equ 170h
CMDS        equ 180h
JAMMER      equ 1E0h
TYPE_DISPLAYS equ 6
TYPE_RWR    equ 8
TYPE_CMDS   equ 10
TYPE_JAMMER equ 16
BUTTON_JAM  equ 64                  ; on CTRL + 15
JAM_WIDTH   equ 5 * 6 + 2           ; 'JAM N' and a margin, top right
BUTTON_CHAFF equ 64
BUTTON_FLARE equ 128
CM_WIDTH    equ 7 * 6 + 2           ; 'C24 F24' and a margin, bottom right
IRQ_VECTOR  equ 0Dh
IRQ_BIT     equ 20h

SLOTS       equ 16                  ; lines in the receiver's table
SCALES      equ 5                   ; ranges at the edge (the table 'scales')
RINGS       equ 4                   ; a ring every quarter of the range
KEY_IN      equ 1                   ; the screen keys
KEY_OUT     equ 2
KEY_SOUND   equ 3
LABEL_X     equ 2                   ; the range, top left
LABEL_Y     equ 2
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

signature   db 'XPRW15'             ; 103h
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
    cmp byte [cm_present], 0
    je .keys
    call dispense
.keys:
    cmp byte [jam_present], 0
    je .screen_keys
    call jam
.screen_keys:
    mov dx, DISPLAYS + 8            ; a screen key since the last look?
    in al, dx
    cmp al, [keys_seen]
    je .line
    mov [keys_seen], al
    mov dx, DISPLAYS + 10
    in al, dx
    cmp al, [my_screen]
    jne .line
    mov dx, DISPLAYS + 9
    in al, dx
    call screen_key
.line:
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
    test dh, 16                     ; guiding a missile at us: the diamond blinks
    jz .steady
    test byte [ticks], SLOTS
    jnz .compare
    mov dh, 2
    jmp .kind
.steady:
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

; The pilot's chaff and flare buttons: one throw on each press; and the
; counts left, looked at every 8th step and drawn again if they changed
dispense:
    mov dx, CTRL + 14
    in al, dx
    mov ah, [cm_buttons]
    mov [cm_buttons], al
    not ah
    and al, ah                      ; pressed since the last look
    and al, BUTTON_CHAFF | BUTTON_FLARE
    jz .count
    mov cl, 6
    shr al, cl                      ; bit 0 chaff, bit 1 a flare
    mov dx, CMDS + 4
    out dx, al
.count:
    test byte [ticks], 7
    jnz .done
    mov dx, CMDS + 2
    in ax, dx                       ; AL chaff, AH flares
    cmp ax, [cm_shown]
    je .done
    push ax
    call put_counts                 ; the old counts off
    pop ax
    mov [cm_shown], ax
    call put_counts                 ; the new ones on
.done:
    ret

; The pilot's jammer button: the next mode on each press; every 8th step
; the beam aimed at the worst radar on the receiver (all round with none),
; and the mode drawn again if it changed
jam:
    mov dx, CTRL + 15
    in al, dx
    mov ah, [jam_buttons]
    mov [jam_buttons], al
    not ah
    and al, ah                      ; pressed since the last look
    test al, BUTTON_JAM
    jz .aim
    mov dx, JAMMER + 2              ; off, noise, deception (if it can), off
    in al, dx
    inc al
    cmp al, 2
    jb .set
    ja .off
    test byte [jam_abilities], 2
    jnz .set
.off:
    xor al, al
.set:
    out dx, al
.aim:
    test byte [ticks], 7
    jnz .done
    call threat
    jc .all_round
    mov dx, JAMMER + 4
    out dx, ax
    xor ax, ax
    mov dx, JAMMER + 6
    out dx, ax
    mov al, 1
    jmp .point
.all_round:
    xor al, al
.point:
    mov dx, JAMMER + 8
    out dx, al
    mov dx, JAMMER + 2
    in al, dx
    cmp al, [jam_shown]
    je .done
    push ax
    call put_jam                    ; the old mode off
    pop ax
    mov [jam_shown], al
    call put_jam                    ; the new one on
.done:
    ret

; The bearing (AX) of the radar guiding a missile at the ship, or else of
; one that has it locked; CF set if there is none
threat:
    mov dx, RWR + 2
    in al, dx
    xor ah, ah
    mov cx, ax
    jcxz .none
    push ds
    mov ds, [cs:table_seg]
    mov si, [cs:table_off]
    xor bl, bl                      ; the worst so far: 0 none, 1 locked, 2 guiding
.entry:
    mov al, [si + 7]
    test al, 8
    jnz .next                       ; a missile's plume: nothing to aim at
    mov ah, 2
    test al, 16
    jnz .rank
    mov ah, 1
    test al, 1
    jz .next
.rank:
    cmp ah, bl
    jbe .next
    mov bl, ah
    mov di, [si + 2]
.next:
    add si, 8
    loop .entry
    pop ds
    test bl, bl
    jz .none
    mov ax, di
    clc
    ret
.none:
    stc
    ret

; The jammer's mode in jam_shown, top right (nothing while it is off)
put_jam:
    mov al, [jam_shown]
    test al, al
    jz .none
    mov si, s_jam_noise
    cmp al, 1
    je .text
    mov si, s_jam_deceive
.text:
    mov es, [screen_seg]
    mov cx, [scr_w]
    sub cx, JAM_WIDTH
    mov dx, LABEL_Y
    jmp xor_text
.none:
    ret

; The counts in cm_shown, 'C' chaff and 'F' flares, XORed bottom right
; (nothing while none is shown yet)
put_counts:
    cmp word [cm_shown], 0FFFFh
    je .none
    push ds
    pop es
    mov di, cm_text
    mov al, 'C'
    stosb
    mov al, [cm_shown]
    call put_count
    mov al, ' '
    stosb
    mov al, 'F'
    stosb
    mov al, [cm_shown + 1]
    call put_count
    xor al, al
    stosb
    mov es, [screen_seg]
    mov si, cm_text
    mov cx, [scr_w]
    sub cx, CM_WIDTH
    mov dx, [scr_h]
    sub dx, 7
    jmp xor_text
.none:
    ret

; AL a count (99 at most shown): its digits at ES:DI
put_count:
    cmp al, 99
    jbe .fits
    mov al, 99
.fits:
    xor ah, ah
    mov bl, 10
    div bl                          ; AL tens, AH units
    test al, al
    jz .units
    add al, '0'
    stosb
.units:
    mov al, ah
    add al, '0'
    stosb
    ret

; bearing, range -> want_x, want_y: out from the middle as far as the
; range, kept between just off the ship and just inside the edge
place:
    mov ax, [range]
    cmp ax, [edge_range]
    jb .inside
    mov ax, [edge_range]
.inside:
    mul word [radius]
    div word [edge_range]           ; AX = pixels out
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
    add cx, SYMBOL_HALF - 2
    add dx, SYMBOL_HALF - 2
    jmp xor_char

; AL a character (A to Z, 0 to 9; anything else draws nothing): XORs it,
; 5 x 5, with its top left at (CX, DX). Keeps CX and DX.
xor_char:
    push cx
    push dx
    sub al, '0'
    cmp al, 9
    jbe .digit
    sub al, 'A' - '0'
    cmp al, 25
    ja .done
    jmp .look
.digit:
    add al, 26                      ; the digits come after the letters
.look:
    mov bl, 10
    mul bl
    mov si, ax
    add si, font
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
    pop dx
    pop cx
    ret

; DS:SI text (ASCIIZ): XORs it from (CX, DX), 6 pixels a character
xor_text:
    lodsb
    test al, al
    jz .done
    push si
    call xor_char
    pop si
    add cx, 6
    jmp xor_text
.done:
    ret

; A screen key for the RWR screen: AL the key
screen_key:
    mov es, [screen_seg]
    cmp al, KEY_SOUND
    je .sound
    mov bl, [scale]
    cmp al, KEY_IN
    jne .out
    cmp bl, SCALES - 1
    jae .done
    inc bl
    jmp .rescale
.out:
    cmp al, KEY_OUT
    jne .done
    test bl, bl
    jz .done
    dec bl
.rescale:
    push bx
    call put_label                  ; the old range off
    pop bx
    mov [scale], bl
    call put_label                  ; the new one on (the symbols move as their lines come round)
    ret
.sound:
    xor byte [muted], 1
    mov al, [muted]
    mov dx, RWR + 5
    out dx, al
    mov si, s_mute
    mov cx, LABEL_X
    mov dx, [scr_h]
    sub dx, 7
    jmp xor_text
.done:
    ret

; The range of the scale in use: edge_range, and its label XORed top left
put_label:
    mov bl, [scale]
    xor bh, bh
    mov si, bx
    shl si, 1
    mov ax, [scales + si]
    mov [edge_range], ax
    mov si, bx                      ; its label: 3 characters a scale
    shl si, 1
    add si, bx
    shl si, 1
    add si, labels
    mov cx, LABEL_X
    mov dx, LABEL_Y
    jmp xor_text

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
            dw 03F00h, 0C3C0h, 0CCC0h, 0F0C0h, 03F00h    ; 0
            dw 00C00h, 03C00h, 00C00h, 00C00h, 03F00h    ; 1
            dw 0FF00h, 000C0h, 03F00h, 0C000h, 0FFC0h    ; 2
            dw 0FF00h, 000C0h, 00F00h, 000C0h, 0FF00h    ; 3
            dw 0C300h, 0C300h, 0FFC0h, 00300h, 00300h    ; 4
            dw 0FFC0h, 0C000h, 0FF00h, 000C0h, 0FF00h    ; 5
            dw 03F00h, 0C000h, 0FF00h, 0C0C0h, 03F00h    ; 6
            dw 0FFC0h, 000C0h, 00300h, 00C00h, 00C00h    ; 7
            dw 03F00h, 0C0C0h, 03F00h, 0C0C0h, 03F00h    ; 8
            dw 03F00h, 0C0C0h, 03FC0h, 000C0h, 03F00h    ; 9

scales      dw 8000, 6000, 4000, 2000, 1000
labels      db '8K', 0, 0, 0, 0     ; 6 bytes a scale
            db '6K', 0, 0, 0, 0
            db '4K', 0, 0, 0, 0
            db '2K', 0, 0, 0, 0
            db '1K', 0, 0, 0, 0
s_mute      db 'MUTE', 0
cm_text     db 'C99 F99', 0
s_jam_noise db 'JAM N', 0
s_jam_deceive db 'JAM D', 0

slot        db 0
ticks       db 0
scale       db 0                    ; which of 'scales'
muted       db 0
cm_present  db 0                    ; the ship has a dispenser
cm_buttons  db 0                    ; the pilot's buttons, last look
cm_shown    dw 0FFFFh               ; the counts drawn: chaff, flares
jam_present db 0                    ; the ship has a jammer
jam_abilities db 0                  ; what it can do (bit 1 deception)
jam_buttons db 0                    ; the pilot's buttons (+15), last look
jam_shown   db 0                    ; the mode drawn (0 off: nothing)
keys_seen   db 0                    ; the display card's key count, last look
my_screen   db 0FFh                 ; the RWR screen's place in the screen table
edge_range  dw 8000
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
    mov ax, bx                      ; its place in the screen table
    mov dx, DISPLAYS + 4
    push ax
    in ax, dx
    mov cx, ax
    pop ax
    sub ax, cx
    mov cl, 4
    shr ax, cl
    mov [my_screen], al
    mov dx, DISPLAYS + 8
    in al, dx
    mov [keys_seen], al
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
    mov es, [screen_seg]
    call put_label
    xor al, al                      ; the tones on
    mov dx, RWR + 5
    out dx, al
    mov dx, CMDS                    ; a dispenser to work?
    in al, dx
    cmp al, TYPE_CMDS
    jne .no_dispenser
    mov byte [cm_present], 1
    mov dx, CTRL + 14
    in al, dx
    mov [cm_buttons], al
.no_dispenser:
    mov dx, JAMMER                  ; a jammer to work?
    in al, dx
    cmp al, TYPE_JAMMER
    jne .no_jammer
    mov byte [jam_present], 1
    mov dx, JAMMER + 3
    in al, dx
    mov [jam_abilities], al
    mov dx, CTRL + 15
    in al, dx
    mov [jam_buttons], al
.no_jammer:

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
    call unlink                     ; out of the IRQ 5 chain, wherever it is
    xor al, al                      ; the tones back on
    mov dx, RWR + 5
    out dx, al
    cmp byte [es:jam_present], 0    ; the jammer off
    je .silent
    mov dx, JAMMER + 2
    out dx, al
.silent:
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

; Looks for the resident copy along the IRQ 5 chain, through the ship's
; other programs (each keeps its signature, 'XP' and four more, at 103h
; and the handler it calls at 109h, as this one does): ZF set and ES its
; segment if found, [prev_seg] the program before it (0: it is first).
; A program in the way that is not a ship's ends the search.
find_resident:
    mov word [prev_seg], 0
    mov ax, 3500h + IRQ_VECTOR
    int 21h
.next:
    mov si, signature
    mov di, si
    mov cx, 6
    repe cmpsb
    je .done
    cmp word [es:103h], 'XP'        ; another of the ship's programs
    jne .no
    mov [prev_seg], es
    les bx, [es:109h]               ; the one it calls
    mov ax, es
    or ax, bx
    jnz .next
.no:
    or al, 1                        ; ZF = 0
.done:
    ret

; Takes the resident copy (ES) out of the IRQ 5 chain, wherever it is in
; it: the program before it calls what it called, or the vector does
unlink:
    push ds
    cli
    mov ax, [es:old_irq]
    mov dx, [es:old_irq + 2]
    cmp word [prev_seg], 0
    je .first
    mov ds, [prev_seg]
    mov [109h], ax
    mov [10Bh], dx
    jmp .done
.first:
    mov ds, dx
    mov dx, ax
    mov ax, 2500h + IRQ_VECTOR
    int 21h
.done:
    sti
    pop ds
    ret

prev_seg    dw 0

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
                db 'Chaff and flares on the pilot', 39, 's buttons.', 13, 10
                db 'RWR OFF removes it.', 13, 10, '$'
msg_already     db 'Threat warning is already resident.', 13, 10, '$'
msg_not_running db 'Threat warning is not resident, or a program that is not', 13, 10
                db "one of the ship's took IRQ 5 after it.", 13, 10, '$'
msg_removed     db 'Threat warning removed.', 13, 10, '$'
msg_no_device   db 'RWR: the warning receiver or the display card is not', 13, 10
                db 'on the bus.', 13, 10, '$'
msg_no_screen   db 'RWR: the ship has no RWR screen (32 x 32 at least).', 13, 10, '$'
