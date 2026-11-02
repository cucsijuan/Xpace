; tune.asm - HKP Generator Tuner 1.0 (TUNE.COM)
;
;   TUNE        stays resident and tunes the cruise generator from the
;               pilot's buttons, against an interdictor's beam
;   TUNE OFF    takes it out of memory
;
; An interdictor's beam thickens the medium at a frequency that wanders. The
; generator cancels the beam only near that frequency, and past what it
; can bear it drops out. With a beam on the ship this program takes the RADAR
; screen and sweeps the two waves across it like a two-trace oscilloscope, the
; beam's above and the generator's below: more waves above than below, tune
; up; fewer, tune down; alike, it is tuned. A tap of a button moves the frequency
; a little, and held it goes faster; a needle under the traces shows where it is
; at once, the traces only as the sweep gets there. Under them, how much of the
; beam the generator cancels and the strain on it. The screen goes back when the
; beam goes.
;
; It reads no frequency from the beam: nothing on the bus gives it, only the
; wave as received. A program that works it out from the wave can tune by itself.
;
; The devices (16 ports each):
;
;   150h  display card       +2 segment  +4 screen table  +6 screens; an entry
;                            is 16 bytes: name(6) +6 mode +9 lent +10 width
;                            +12 height +14 memory
;   220h  cruise generator   +0 type 20  +2 status: bit 4 a beam is on it
;                            +10 its frequency, where to tune it  +11 buttons:
;                            bit 0 up, bit 1 down  +12 strain  +13 cancels
;                            +14 the beam's wave  +15 its own wave

org 100h

DISPLAYS    equ 150h
CRUISE      equ 220h
TYPE_CRUISE equ 20
STATUS_BEAM equ 16
LENT_MARK   equ 'T'
WIDTH       equ 160
HEIGHT      equ 128
PITCH       equ 40
COLUMNS     equ 70                  ; of the sweep, two pixels wide: one a step
MARGIN      equ 20                  ; pixels left of it, for the labels
GAP         equ 4                   ; columns kept clear ahead of the sweep
BEAM_ROW    equ 26                  ; the traces' middles
OWN_ROW     equ 74
LOCK_ROW    equ 110
LOAD_ROW    equ 123
BAR_ROWS    equ 4
NEEDLE_ROW  equ 98
NEEDLE_ROWS equ 3
IRQ_VECTOR  equ 0Dh
IRQ_BIT     equ 20h

    jmp start

; ======================================================================
; The resident part
; ======================================================================

signature   db 'XPTU10'             ; 103h
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

step:
    mov dx, CRUISE + 2
    in al, dx
    test al, STATUS_BEAM
    jnz .beam
    cmp byte [lent], 0
    je .done
    mov byte [lent], 0              ; no beam: the screen goes back
    les bx, [entry]
    mov byte [es:bx + 9], 0
.done:
    ret
.beam:
    les bx, [entry]
    cmp byte [lent], 0
    jne .ours
    cmp byte [es:bx + 9], 0
    jne .done                       ; another program has it
    mov byte [es:bx + 9], LENT_MARK
    mov byte [es:bx + 6], 1         ; graphics
    mov es, [screen]
    call take_over
    jmp .picture
.ours:
    mov es, [screen]
.picture:
    call buttons
    call needle
    call figure
    mov dx, CRUISE + 13
    in al, dx
    mov si, lock_width
    mov dx, LOCK_ROW
    call bar
    mov dx, CRUISE + 12
    in al, dx
    mov si, load_width
    mov dx, LOAD_ROW
    jmp bar

; A clean screen, the labels, and nothing of the figure yet
take_over:
    mov byte [lent], 1
    xor di, di
    xor ax, ax
    mov cx, PITCH * HEIGHT / 2
    rep stosw
    push es
    push ds
    pop es
    mov di, beam_trace
    mov cx, COLUMNS * 2
    xor al, al
    rep stosb
    pop es
    mov byte [sweep], 0
    mov byte [hold], 0
    mov word [needle_x], MARGIN
    mov word [lock_width], 0
    mov word [load_width], 0
    mov dx, CRUISE + 10
    in al, dx
    mov [tune], al
    mov si, word_beam
    mov cx, 2
    xor dx, dx
    call label
    mov si, word_you
    mov cx, 2
    mov dx, OWN_ROW - 24
    call label
    mov si, word_lock
    mov cx, 2
    mov dx, LOCK_ROW - 7
    call label
    mov si, word_strain
    mov cx, 2
    mov dx, LOAD_ROW - 7
    jmp label

; The pilot's buttons move where the generator is told to tune: a step at a
; time at first, faster the longer they are held
buttons:
    mov dx, CRUISE + 11
    in al, dx
    and al, 3
    jnz .held
    mov byte [hold], 0
    ret
.held:
    mov cl, 1
    cmp byte [hold], 8
    jb .step
    mov cl, 2
    cmp byte [hold], 24
    jb .step
    mov cl, 4
    jmp .moving
.step:
    inc byte [hold]
.moving:
    mov ah, [tune]
    test al, 1
    jz .down
    add ah, cl
    jnc .down
    mov ah, 255
.down:
    test al, 2
    jz .set
    sub ah, cl
    jnc .set
    xor ah, ah
.set:
    mov [tune], ah
    mov al, ah
    mov dx, CRUISE + 10
    out dx, al
    ret

; The needle: where the generator is told to tune, along the sweep's width
needle:
    mov al, [tune]
    xor ah, ah
    shr ax, 1
    add ax, MARGIN
    cmp ax, [needle_x]
    je .done
    push ax
    mov cx, [needle_x]
    xor al, al
    call needle_mark
    pop cx
    mov [needle_x], cx
    mov al, 0FFh
    call needle_mark
.done:
    ret

; The needle at column CX to the value AL
needle_mark:
    mov dx, NEEDLE_ROW
    mov bp, NEEDLE_ROWS
.row:
    call pixel
    inc cx
    call pixel
    dec cx
    inc dx
    dec bp
    jnz .row
    ret

; One step of the sweep, on both traces
figure:
    xor bh, bh
    mov bl, [sweep]
    mov dx, CRUISE + 14
    in al, dx
    mov si, beam_trace
    mov bp, BEAM_ROW
    call trace_step
    mov dx, CRUISE + 15
    in al, dx
    mov si, own_trace
    mov bp, OWN_ROW
    call trace_step
    inc bl
    cmp bl, COLUMNS
    jb .kept
    xor bl, bl
.kept:
    mov [sweep], bl
    ret

; AL a signed sample, for column BX of the trace SI (its rows, 0 none) round row BP:
; drawn there, and the column GAP ahead cleared
trace_step:
    push bx
    cbw                             ; 3/16 of it: 23 at most
    sar ax, 1
    sar ax, 1
    sar ax, 1
    mov di, ax
    sar di, 1
    add ax, di
    sub bp, ax
    mov dx, bp
    mov [si + bx], dl
    mov al, 0FFh
    call column
    add bx, GAP
    cmp bx, COLUMNS
    jb .ahead
    sub bx, COLUMNS
.ahead:
    mov dl, [si + bx]
    or dl, dl
    jz .done
    mov byte [si + bx], 0
    xor dh, dh
    xor al, al
    call column
.done:
    pop bx
    ret

; Column BX of a trace at row DX to the value AL
column:
    mov cx, bx
    shl cx, 1
    add cx, MARGIN
    call pixel
    inc cx
    jmp pixel

; (CX, DX) to the value AL (0 off, FFh on); all but DI kept
pixel:
    push ax
    push bx
    push cx
    push dx
    mov bl, al
    mov ax, PITCH
    mul dx
    mov di, cx
    shr di, 1
    shr di, 1
    add di, ax
    and cl, 3
    shl cl, 1
    mov al, 0C0h
    shr al, cl
    mov ah, al
    not ah
    and [es:di], ah
    and al, bl
    or [es:di], al
    pop dx
    pop cx
    pop bx
    pop ax
    ret

; AL 0 to 255: the bar on the rows from DX to that share of the width; SI the
; word that keeps how wide it is drawn. Only what changed is touched.
bar:
    xor ah, ah
    mov cl, 3                       ; 5/8: 159 at most
    mov bx, ax
    shl ax, 1
    shl ax, 1
    add ax, bx
    shr ax, cl
    mov bp, ax
    mov cx, [si]
    cmp cx, bp
    je .done
    mov [si], bp
    mov bl, 0FFh
    jb .span
    xchg cx, bp                     ; shorter: from the new end to the old, off
    mov bl, 0
.span:
    cmp cx, bp
    jae .done
    push dx
    mov ah, BAR_ROWS
.row:
    mov al, bl
    call pixel
    inc dx
    dec ah
    jnz .row
    pop dx
    inc cx
    jmp .span
.done:
    ret

; SI a word (addresses of letters, 0 at the end): from column CX, row DX
label:
.letter:
    lodsw
    or ax, ax
    jz .done
    push si
    mov si, ax
    call letter
    pop si
    add cx, 4
    jmp .letter
.done:
    ret

; SI the letter's five rows (bit 2 left), at (CX, DX)
letter:
    push dx
    mov bp, 5
.row:
    lodsb
    mov ah, al
    test ah, 4
    jz .middle
    mov al, 0FFh
    call pixel
.middle:
    inc cx
    test ah, 2
    jz .right
    mov al, 0FFh
    call pixel
.right:
    inc cx
    test ah, 1
    jz .next
    mov al, 0FFh
    call pixel
.next:
    sub cx, 2
    inc dx
    dec bp
    jnz .row
    pop dx
    ret

g_a         db 2, 5, 7, 5, 5
g_b         db 6, 5, 6, 5, 6
g_e         db 7, 4, 6, 4, 7
g_m         db 5, 7, 7, 5, 5
g_u         db 5, 5, 5, 5, 7
g_y         db 5, 5, 2, 2, 2
g_c         db 7, 4, 4, 4, 7
g_i         db 7, 2, 2, 2, 7
g_k         db 5, 5, 6, 5, 5
g_l         db 4, 4, 4, 4, 7
g_n         db 5, 7, 7, 5, 5
g_o         db 7, 5, 5, 5, 7
g_r         db 6, 5, 6, 5, 5
g_s         db 7, 4, 7, 1, 7
g_t         db 7, 2, 2, 2, 2
word_beam   dw g_b, g_e, g_a, g_m, 0
word_you    dw g_y, g_o, g_u, 0
word_lock   dw g_l, g_o, g_c, g_k, 0
word_strain dw g_s, g_t, g_r, g_a, g_i, g_n, 0

entry       dw 0, 0                 ; the RADAR screen's entry
screen      dw 0                    ; its memory's segment
lent        db 0
tune        db 128
sweep       db 0
hold        db 0                    ; steps the tuning button has been held
needle_x    dw MARGIN
lock_width  dw 0
load_width  dw 0
beam_trace  times COLUMNS db 0
own_trace   times COLUMNS db 0

resident_end:

; ======================================================================
; Installing and removing
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
    mov dx, CRUISE                  ; a ship without these has no use for it
    in al, dx
    cmp al, TYPE_CRUISE
    je .generator
    jmp quit
.generator:
    mov si, n_radar
    call find_screen
    jnc .screen
    jmp quit
.screen:
    cmp word [es:bx + 10], WIDTH
    je .wide
    jmp quit
.wide:
    cmp word [es:bx + 12], HEIGHT
    je .high
    jmp quit
.high:
    mov [entry], bx
    mov [entry + 2], es
    mov ax, [es:bx + 14]
    mov cl, 4
    shr ax, cl
    mov cx, es
    add ax, cx
    mov [screen], ax

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
    cmp byte [es:lent], 0           ; the screen back, if it had it
    je .free
    push es
    les bx, [es:entry]
    mov byte [es:bx + 9], 0
    pop es
.free:
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

n_radar     db 'RADAR '

quit_message:
    mov ah, 09h
    int 21h
quit:
    mov ax, 4C01h
    int 21h

msg_installed   db 'Generator tuner resident.', 13, 10, '$'
msg_already     db 'Generator tuner is already resident.', 13, 10, '$'
msg_not_running db 'Generator tuner is not resident, or a program that is not', 13, 10
                db "one of the ship's took IRQ 5 after it.", 13, 10, '$'
msg_removed     db 'Generator tuner removed.', 13, 10, '$'
