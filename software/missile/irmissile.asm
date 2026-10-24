; irmissile.asm - CucsiCorp Heat Seeker 1.0 (IRMISSILE.BIN, firmware)
;
; The program in a heat seeking missile's own computer: a bare 8086 with
; no BIOS and no DOS, like MISSILE.BIN. It starts at 100h, points the
; clock's interrupt at its handler and waits in HLT; the clock ticks sixty
; times a second.
;
; Nobody guides it from outside. Its head, on a gimbal, follows the
; centre of the heat it sees and tells this program which way it points
; (a direction, x y z in 1/16384, in the missile's stabilized axes); it
; knows neither the range nor the closing speed. Every tick:
;
;   - how far the head turned since the last tick is how fast the line of
;     sight to the target turns; smoothed over a few ticks, it is what the
;     motor is asked to push across: NAV_GAIN times the turn, which is
;     proportional navigation for a closing speed of about 600 m/s
;     (the head cannot tell more);
;   - while the fuel lasts beyond FUEL_RESERVE it also pushes along the
;     line of sight (BOOST), to get there sooner; the reserve is for the
;     turns at the end;
;   - the first SETTLE_STEPS after the head first sees something, it only
;     pushes ahead: the head is still turning onto the target;
;   - with nothing in the head's view it holds its last turn; after
;     LOST_STEPS it gives up and coasts;
;   - until the head first sees something (fired with no heat in view)
;     it searches: it pushes along where the head points, while the fuel
;     lasts beyond the reserve, and follows the first heat that comes into
;     the head's view.
;
; Whatever the head follows, this program follows: if a flare pulls the
; head away from the target, the missile goes to the flare.
;
; The ports (words little-endian):
;
;   100h  +1 seeker: counts up every tick the head sees something
;         +2 +4 +6 which way the head points, x y z (1/16384; always
;                  there, the gimbal's angles)
;         +8 +10 +12 write: the acceleration wanted, x y z (1/16 m/s^2)
;         +14 write: status, bit 0 guiding, 1 the head sees nothing, 2 lost
;         +16 fuel: the speed change left (m/s)
;         +18 write: time to go (always 0FFFFh: it does not know)

org 100h

PORTS       equ 100h
P_SEEKER    equ PORTS + 1
P_LOOK      equ PORTS + 2
P_COMMAND   equ PORTS + 8
P_STATUS    equ PORTS + 14
P_FUEL      equ PORTS + 16
P_TGO       equ PORTS + 18
TICK_VECTOR equ 08h

NAV_GAIN    equ 105                 ; 3 x 600 m/s, per 1/16384 a tick, in 1/16 m/s^2
TURN_MAX    equ 150                 ; 1/16384 a tick: the most the head turns
SMOOTH      equ 2                   ; the turn smoothed over 2^SMOOTH ticks
SETTLE_STEPS equ 15                 ; a quarter of a second
LOST_STEPS  equ 60                  ; a second without seeing anything: lost
BOOST       equ 2400                ; 150 m/s^2, in 1/16 m/s^2
FUEL_RESERVE equ 400                ; m/s kept for the end

ST_GUIDING  equ 1
ST_NO_WORD  equ 2
ST_LOST     equ 4

start:
    cli
    xor ax, ax
    mov es, ax
    mov word [es:TICK_VECTOR * 4], on_tick
    mov [es:TICK_VECTOR * 4 + 2], cs
    push cs
    pop es
    sti
idle:
    hlt
    jmp idle

on_tick:
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

step:
    mov dx, P_SEEKER
    in al, dx
    cmp al, [counter]
    je .no_word
    mov [counter], al               ; the head sees something
    mov word [since], 0
    call read_look
    cmp byte [started], 0
    jne .turned
    mov byte [started], 1
    mov word [settle], SETTLE_STEPS
    call remember
    jmp .guide
.turned:
    call turn_rate
    call remember
    jmp .guide
.no_word:
    cmp byte [started], 0
    je .quiet
    inc word [since]
    cmp word [since], LOST_STEPS
    ja .lost
.guide:
    call guide
    jmp report
.lost:
    mov byte [status], ST_LOST
    jmp .off
.quiet:
    mov byte [status], 0            ; searching: ahead, where the head points
    call read_look
    xor ax, ax
    mov [cmd], ax
    mov [cmd + 2], ax
    mov [cmd + 4], ax
    call boost
    jmp report
.off:
    xor ax, ax
    mov [cmd], ax
    mov [cmd + 2], ax
    mov [cmd + 4], ax
report:
    mov si, cmd
    mov dx, P_COMMAND
    mov cx, 3
.command:
    lodsw
    out dx, ax
    add dx, 2
    loop .command
    mov al, [status]
    mov dx, P_STATUS
    out dx, al
    mov ax, 0FFFFh
    mov dx, P_TGO
    out dx, ax
    ret

; The head's turn since the last tick, per axis, into the smoothed
; command: rate = rate + (NAV_GAIN turn - rate) / 2^SMOOTH
turn_rate:
    xor bx, bx
.axis:
    mov ax, [look + bx]
    sub ax, [prev + bx]
    cmp ax, TURN_MAX
    jle .not_high
    mov ax, TURN_MAX
.not_high:
    cmp ax, -TURN_MAX
    jge .not_low
    mov ax, -TURN_MAX
.not_low:
    mov cx, NAV_GAIN
    imul cx                         ; fits: 150 x 105
    sub ax, [rate + bx]
    mov cl, SMOOTH
    sar ax, cl
    add [rate + bx], ax
    add bx, 2
    cmp bx, 6
    jb .axis
    ret

read_look:
    mov dx, P_LOOK
    mov di, look
    mov cx, 3
.look:
    in ax, dx
    stosw
    add dx, 2
    loop .look
    ret

remember:
    mov si, look
    mov di, prev
    mov cx, 3
    rep movsw
    ret

guide:
    mov al, ST_GUIDING
    cmp word [since], 0
    je .heard
    or al, ST_NO_WORD
.heard:
    mov [status], al
    xor bx, bx
.axis:
    xor ax, ax
    cmp word [settle], 0            ; settling: no turning yet
    jne .put
    mov ax, [rate + bx]
.put:
    mov [cmd + bx], ax
    add bx, 2
    cmp bx, 6
    jb .axis
    cmp word [settle], 0
    je boost
    dec word [settle]
    ; fall through

boost:
    mov dx, P_FUEL                  ; fuel to spare: push along the line of sight
    in ax, dx
    cmp ax, FUEL_RESERVE
    jbe .done
    xor bx, bx
.push:
    mov ax, [look + bx]             ; BOOST x the direction (1/16384)
    mov cx, BOOST
    imul cx
    mov cx, 16384
    idiv cx
    add [cmd + bx], ax
    add bx, 2
    cmp bx, 6
    jb .push
.done:
    ret

; ---------- data ----------

started     db 0
counter     db 0
status      db 0
since       dw 0                    ; ticks since the head last saw something
settle      dw 0
look        times 3 dw 0            ; where the head points (1/16384)
prev        times 3 dw 0            ; where it pointed the tick before
rate        times 3 dw 0            ; the smoothed command (1/16 m/s^2)
cmd         times 3 dw 0            ; the acceleration wanted (1/16 m/s^2)
