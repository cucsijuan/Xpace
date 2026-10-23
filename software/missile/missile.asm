; missile.asm - CucsiCorp Missile Guidance 1.0 (MISSILE.BIN, firmware)
;
; The program in the missile's own computer: a bare 8086 with no BIOS and
; no DOS. It starts at 100h, points the clock's interrupt at its handler
; and waits in HLT; the clock ticks sixty times a second.
;
; The ship guides it over the datalink: every time the ship's radar
; measures the target, the missile is told where the target is from it
; (x, y, z in metres, in the missile's stabilized axes; which way each
; axis points does not matter to the guidance). The measurements are as
; noisy as the ship's radar makes them. Every tick:
;
;   - an alpha-beta filter smooths the positions and works out how fast
;     the target moves relative to the missile; what the missile's own
;     motor did (its accelerometers) goes into the prediction, so its own
;     acceleration is not taken for the target's;
;   - proportional navigation: with R the target's place, V its velocity
;     (both relative), the closing speed Vc = -R.V / |R| and the time to
;     go t = |R| / Vc, the miss if nothing more were done is
;     ZEM = R + V t, and the motor is asked for N ZEM / t^2 to cancel it;
;   - while it closes slower than CLOSE_WANTED it also pushes toward the
;     target (BOOST), to get there sooner;
;   - with no word from the ship it flies on its estimate; after
;     LOST_STEPS without one it gives up and coasts.
;
; The motor gives what it can of what is asked (it has a top
; acceleration and runs out of fuel), and the fuse is the missile's
; hardware: neither is this program's business.
;
; The ports (words little-endian):
;
;   100h  +1 datalink: counts up with every new message
;         +2 +4 +6 the target from the missile, x y z (m)
;         +8 +10 +12 write: the acceleration wanted, x y z (1/16 m/s^2)
;         +14 write: status, bit 0 guiding, 1 no word from the ship, 2 lost
;         +16 fuel: the speed change left (m/s)
;         +18 write: time to go (1/8 s, 0FFFFh unknown), sent to the ship
;         +20 +22 +24 accelerometers: what the motor gave the last tick
;                     (1/16 m/s^2)

org 100h

PORTS       equ 100h
P_LINK      equ PORTS + 1
P_TARGET    equ PORTS + 2
P_COMMAND   equ PORTS + 8
P_STATUS    equ PORTS + 14
P_TGO       equ PORTS + 18
P_ACCEL     equ PORTS + 20
TICK_VECTOR equ 08h

NAV_GAIN    equ 768                 ; N = 3, in 1/256
ALPHA       equ 31                  ; 0.12, in 1/256
BETA_DT     equ 118                 ; 0.0077 * 60, in 1/256
SETTLE_STEPS equ 30                 ; half a second for the filter to settle
LOST_STEPS  equ 120                 ; two seconds without a word: lost
CLOSE_WANTED equ 2800               ; 700 m/s, in 1/4 m/s
CLOSE_MIN   equ 80                  ; 20 m/s: slower, there is no time to go
BOOST       equ 2400                ; 150 m/s^2, in 1/16 m/s^2
COMMAND_MAX equ 16000               ; 1000 m/s^2: far beyond any motor

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
    mov dx, P_ACCEL                 ; what the motor gave
    mov di, accel
    mov cx, 3
.accel:
    in ax, dx
    stosw
    add dx, 2
    loop .accel

    mov dx, P_LINK
    in al, dx
    cmp al, [counter]
    je .no_word
    mov [counter], al               ; a new message from the ship
    mov word [since], 0
    mov dx, P_TARGET
    mov di, meas
    mov cx, 3
.meas:
    in ax, dx
    stosw
    add dx, 2
    loop .meas
    cmp byte [started], 0
    jne .update
    mov byte [started], 1
    mov word [settle], SETTLE_STEPS
    call filter_start
    jmp .guide
.update:
    call predict
    call correct
    jmp .guide
.no_word:
    cmp byte [started], 0
    je .quiet
    inc word [since]
    cmp word [since], LOST_STEPS
    ja .lost
    call predict
.guide:
    call guide
    jmp report
.lost:
    mov byte [status], ST_LOST
    jmp .off
.quiet:
    mov byte [status], 0
.off:
    xor ax, ax
    mov [cmd], ax
    mov [cmd + 2], ax
    mov [cmd + 4], ax
    mov word [tgo], 0FFFFh
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
    mov ax, [tgo]
    cmp ax, 0FFFFh
    je .tgo
    mov cl, 3                       ; 1/64 s to 1/8 s
    shr ax, cl
.tgo:
    mov dx, P_TGO
    out dx, ax
    ret

; ---------- the alpha-beta filter ----------
;
; Per axis a position x and a velocity v (1/64 m, 1/64 m/s, 32 bits):
;   predicts    x = x + v dt,  v = v - a dt (the missile's own push)
;   compares    r = measured - x
;   corrects    x = x + ALPHA r,   v = v + (BETA / dt) r

filter_start:                       ; x = the measurement, v = 0
    xor bx, bx
    xor si, si
.axis:
    mov ax, [meas + bx]
    mov cx, 64
    imul cx
    mov [pos + si], ax
    mov [pos + si + 2], dx
    mov word [vel + si], 0
    mov word [vel + si + 2], 0
    add si, 4
    add bx, 2
    cmp bx, 6
    jb .axis
    ret

predict:
    xor bx, bx
    xor si, si
.axis:
    mov ax, [vel + si]              ; x += v / 60
    mov dx, [vel + si + 2]
    mov cx, 60
    idiv cx
    cwd
    add [pos + si], ax
    adc [pos + si + 2], dx
    mov ax, [accel + bx]            ; v -= a / 60 (1/16 m/s^2 to 1/64 m/s
    cwd                             ; a tick: * 4 / 60)
    mov cx, 15
    idiv cx
    cwd
    sub [vel + si], ax
    sbb [vel + si + 2], dx
    add si, 4
    add bx, 2
    cmp bx, 6
    jb .axis
    ret

correct:
    xor bx, bx
    xor si, si
.axis:
    mov ax, [meas + bx]             ; r = measured (m, so * 64) - x
    mov cx, 64
    imul cx
    sub ax, [pos + si]
    sbb dx, [pos + si + 2]
    call clamp16
    mov [residual], ax
    mov cx, ALPHA                   ; x += ALPHA r
    imul cx
    call shift8
    add [pos + si], ax
    adc [pos + si + 2], dx
    mov ax, [residual]              ; v += BETA/dt r
    mov cx, BETA_DT
    imul cx
    call shift8
    add [vel + si], ax
    adc [vel + si + 2], dx
    add si, 4
    add bx, 2
    cmp bx, 6
    jb .axis
    ret

; ---------- proportional navigation ----------

guide:
    xor bx, bx                      ; R (m) and V (1/4 m/s) from the filter
    xor si, si
.state:
    mov ax, [pos + si]
    mov dx, [pos + si + 2]
    mov cx, 6
    call shift_right
    call clamp16
    mov [rel + bx], ax
    mov ax, [vel + si]
    mov dx, [vel + si + 2]
    mov cx, 4
    call shift_right
    call clamp16
    mov [rvel + bx], ax
    mov word [cmd + bx], 0
    add si, 4
    add bx, 2
    cmp bx, 6
    jb .state

    mov al, ST_GUIDING
    cmp word [since], 0
    je .heard
    or al, ST_NO_WORD
.heard:
    mov [status], al
    mov word [tgo], 0FFFFh

    mov bx, [rel]                   ; |R|
    mov bp, [rel + 2]
    call cordic
    mov bp, [rel + 4]
    call cordic
    mov [dist], bx
    test bx, bx
    jnz .somewhere
    ret
.somewhere:
    mov ax, [rel]                   ; Vc = -R.V / |R| (1/4 m/s)
    imul word [rvel]
    mov si, ax
    mov di, dx
    mov ax, [rel + 2]
    imul word [rvel + 2]
    add si, ax
    adc di, dx
    mov ax, [rel + 4]
    imul word [rvel + 4]
    add ax, si
    adc dx, di
    neg dx
    neg ax
    sbb dx, 0
    mov cx, [dist]
    call sdiv
    mov [closing], ax

    cmp word [settle], 0            ; the velocity is not known yet
    je .settled
    dec word [settle]
    jmp boost
.settled:
    cmp ax, CLOSE_MIN
    jg .closing
    jmp boost
.closing:
    mov cx, ax                      ; t = |R| * 256 / Vc (1/64 s)
    mov ax, [dist]
    mov dx, 256
    mul dx
    call sdiv
    mov [tgo], ax
    xor bx, bx
.axis:
    mov ax, [rvel + bx]             ; ZEM = R + V t (V t: 1/256 m)
    imul word [tgo]
    call shift8
    mov si, ax
    mov di, dx
    mov ax, [rel + bx]
    cwd
    add ax, si
    adc dx, di
    call clamp16
    mov cx, 256                     ; a = ZEM * 256 / t * N / t
    imul cx                         ;   = N ZEM / t^2 (1/16 m/s^2)
    mov cx, [tgo]
    call sdiv
    mov cx, NAV_GAIN
    imul cx
    mov cx, [tgo]
    call sdiv
    call limit
    mov [cmd + bx], ax
    add bx, 2
    cmp bx, 6
    jb .axis
    ; fall through

; Too slow: push toward the target as well
boost:
    cmp word [closing], CLOSE_WANTED
    jl .push
    ret
.push:
    xor bx, bx
.axis:
    mov ax, [rel + bx]              ; BOOST R / |R|
    mov cx, BOOST
    imul cx
    mov cx, [dist]
    call sdiv
    add [cmd + bx], ax
    add bx, 2
    cmp bx, 6
    jb .axis
    ret

; ---------- arithmetic ----------

; AX = DX:AX / CX (CX > 0), held to -32767..32767 (a wild value must not
; fault). Keeps CX.
sdiv:
    push bx
    mov bx, cx
    shr bx, 1                       ; the quotient fits if |DX| < CX / 2
    cmp dx, bx
    jge .high
    neg bx
    cmp dx, bx
    jl .low
    idiv cx
    pop bx
    ret
.high:
    mov ax, 32767
    pop bx
    ret
.low:
    mov ax, -32767
    pop bx
    ret

; AX held to -COMMAND_MAX..COMMAND_MAX
limit:
    cmp ax, COMMAND_MAX
    jg .high
    cmp ax, -COMMAND_MAX
    jl .low
    ret
.high:
    mov ax, COMMAND_MAX
    ret
.low:
    mov ax, -COMMAND_MAX
    ret

; DX:AX = DX:AX >> CX, keeping the sign
shift_right:
    sar dx, 1
    rcr ax, 1
    loop shift_right
    ret

; DX:AX = DX:AX / 256, keeping the sign
shift8:
    mov al, ah
    mov ah, dl
    mov dl, dh
    mov dh, dl
    mov cl, 7
    sar dh, cl
    ret

; AX = DX:AX held to -32767..32767
clamp16:
    cmp dx, 0
    jl .negative
    jg .high
    cmp ax, 32767
    ja .high
    ret
.high:
    mov ax, 32767
    ret
.negative:
    cmp dx, -1
    jl .low
    cmp ax, 8001h
    jb .low
    ret
.low:
    mov ax, -32767
    ret

; BX = x, BP = y: BX = the length of (x, y), by CORDIC: turning the vector
; onto the x axis in 14 small steps, shifts and adds only. Changes AX, CX,
; DX, SI, BP.
cordic:
    test bx, bx
    jns .right_half
    neg bx                          ; the left half: turn it round first
    neg bp
.right_half:
    xor dx, dx                      ; small enough not to overflow
.scale:
    cmp bx, 8192
    jge .smaller
    cmp bp, 8192
    jge .smaller
    cmp bp, -8192
    jle .smaller
    jmp .scaled
.smaller:
    sar bx, 1
    sar bp, 1
    inc dx
    jmp .scale
.scaled:
    push dx
    xor si, si
.turn:
    mov cx, si
    shr cl, 1                       ; the step number
    mov ax, bp                      ; y >> i
    sar ax, cl
    mov dx, bx                      ; x >> i
    sar dx, cl
    test bp, bp
    js .up
    add bx, ax                      ; y above: turn clockwise
    sub bp, dx
    jmp .stepped
.up:
    sub bx, ax
    add bp, dx
.stepped:
    add si, 2
    cmp si, 28
    jb .turn
    mov ax, bx                      ; length = x / 1.6468
    mov cx, 39797
    mul cx
    mov bx, dx
    pop cx                          ; undo the scaling
    shl bx, cl
    ret

; ---------- data ----------

started     db 0
counter     db 0
status      db 0
since       dw 0                    ; ticks since the last message
settle      dw 0
residual    dw 0
dist        dw 0                    ; |R| (m)
closing     dw 0                    ; Vc (1/4 m/s)
tgo         dw 0                    ; time to go (1/64 s)
meas        times 3 dw 0            ; the last message (m)
accel       times 3 dw 0            ; the accelerometers (1/16 m/s^2)
rel         times 3 dw 0            ; R (m)
rvel        times 3 dw 0            ; V (1/4 m/s)
cmd         times 3 dw 0            ; the acceleration wanted (1/16 m/s^2)
pos         times 6 dw 0            ; the filter: x per axis (1/64 m, 32 bits)
vel         times 6 dw 0            ;             v per axis (1/64 m/s, 32 bits)
