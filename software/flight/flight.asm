; flight.asm - CucsiCorp Flight Control 1.0 (FLIGHT.COM)
;
; Flies the ship. Sixty times a second the inertial unit interrupts (IRQ 5)
; with a new sample; the program reads it and the pilot's controls, works
; out what every group of thrusters must do, and writes the commands.
;
; Flight assist on: the stick asks for a turn rate and the throttle for a
; forward speed; the strafe controls ask for a sideways speed. Let go and
; the ship stops turning and drifting. Flight assist off: the stick and the
; throttle drive the thrusters directly, and the ship keeps whatever motion
; it has. The pilot's FA button switches between the two.
;
; If this program stops writing, the thruster controller falls back to
; direct control after 100 ms, as if flight assist were off for good.
;
; The devices on the ship's bus (16 ports each, words little-endian):
;
;   100h  pilot's controls   +0 type 1   +2 pitch  +4 roll  +6 yaw
;                            +8 strafe right  +10 strafe up  +12 throttle
;                            (all -32767..32767)  +14 buttons (bit 0 FA)
;   110h  inertial unit      +0 type 2   +2 pitch rate  +4 roll rate
;                            +6 yaw rate (1/1024 rad/s)  +8 right  +10 up
;                            +12 forward speed (1/8 m/s)  +14 status:
;                            reading it acknowledges the sample
;   120h  thrusters          +0 type 3   +2 pitch  +4 roll  +6 yaw torque
;                            +8 right  +10 up  +12 forward force
;                            (-32767..32767 of the most they give)
;                            +14 write: take the commands  +15 mode shown
;
; Esc ends the program.

org 100h

CTRL        equ 100h
IMU         equ 110h
THRUST      equ 120h
TYPE_CTRL   equ 1
TYPE_IMU    equ 2
TYPE_THRUST equ 3
IRQ_VECTOR  equ 0Dh                 ; IRQ 5
IRQ_BIT     equ 20h

; What full stick and full throttle ask for, with flight assist on
MAX_PITCH   equ 1229                ; 1.2 rad/s, in 1/1024 rad/s
MAX_ROLL    equ 2560                ; 2.5 rad/s
MAX_YAW     equ 614                 ; 0.6 rad/s
MAX_SPEED   equ 2000                ; 250 m/s forward, in 1/8 m/s
MAX_STRAFE  equ 800                 ; 100 m/s sideways
GAIN_RATE   equ 96                  ; command for each unit of turn-rate error
GAIN_SPEED  equ 204                 ; command for each unit of speed error

SCREEN_EVERY equ 6                  ; steps between screen updates (10 a second)
ATTR_LABEL  equ 03h
ATTR_VALUE  equ 0Fh
ATTR_TITLE  equ 1Fh
ATTR_ON     equ 0Ah
ATTR_OFF    equ 0Ch
COLS        equ 80

    jmp start

; ======================================================================
; Start and stop
; ======================================================================

start:
    mov dx, CTRL                    ; the three devices must be there
    mov bl, TYPE_CTRL
    call check_device
    mov dx, IMU
    mov bl, TYPE_IMU
    call check_device
    mov dx, THRUST
    mov bl, TYPE_THRUST
    call check_device

    mov ax, 0003h                   ; 80 x 25 text
    int 10h
    mov ah, 01h                     ; no cursor
    mov cx, 2000h
    int 10h
    call draw_labels
    call timer_start

    mov ax, 3500h + IRQ_VECTOR
    int 21h
    mov [old_irq], bx
    mov [old_irq + 2], es
    push ds
    pop es
    mov dx, on_irq
    mov ax, 2500h + IRQ_VECTOR
    int 21h
    in al, 21h
    and al, 0FFh - IRQ_BIT
    out 21h, al

main:
    call check_keys
    cli                             ; STI HLT: no sample lost in between
    cmp byte [tick], 0
    jne .tick
    sti
    hlt
    jmp main
.tick:
    mov byte [tick], 0
    sti
    call step
    jmp main

check_keys:
    mov ah, 01h
    int 16h
    jz .none
    xor ah, ah
    int 16h
    cmp al, 27
    je stop
.none:
    ret

stop:
    in al, 21h
    or al, IRQ_BIT
    out 21h, al
    push ds
    lds dx, [old_irq]
    mov ax, 2500h + IRQ_VECTOR
    int 21h
    pop ds
    call timer_stop
    mov ax, 0003h
    int 10h
    mov dx, msg_stopped
    mov ah, 09h
    int 21h
    mov ax, 4C00h
    int 21h

; The device at port DX must be of type BL
check_device:
    in al, dx
    cmp al, bl
    jne no_device
    ret

no_device:
    mov dx, msg_no_device
    mov ah, 09h
    int 21h
    mov ax, 4C01h
    int 21h

on_irq:
    mov byte [cs:tick], 1
    push ax
    mov al, 20h
    out 20h, al
    pop ax
    iret

; ======================================================================
; One step: sample in, commands out
; ======================================================================

step:
    call timer_read
    mov [t_start], ax

    mov dx, IMU + 14                ; acknowledge the sample
    in al, dx
    mov dx, CTRL + 2
    mov di, controls
    mov cx, 6
    call read_words
    mov dx, CTRL + 14
    in al, dx
    mov ah, [buttons]               ; FA on the press of its button
    mov [buttons], al
    not ah
    and al, ah
    test al, 1
    jz .no_toggle
    xor byte [assist], 1
.no_toggle:
    mov dx, IMU + 2
    mov di, motion
    mov cx, 6
    call read_words

    cmp byte [assist], 0
    je .direct

    ; flight assist: each axis drives its error to zero
    mov si, axes
    mov di, commands
.axis:
    lodsw                           ; the input, as an offset
    test ax, ax
    jz .assisted
    mov bx, ax
    lodsw                           ; ... how much full input asks for
    mov cx, ax
    mov ax, [bx]
    call scale                      ; AX = the setpoint
    mov bx, [si]                    ; ... what the ship is doing
    sub ax, [bx]
    mov cx, [si + 2]                ; ... the gain
    add si, 4
    call times_clamped
    stosw
    jmp .axis

.direct:
    mov si, controls
    mov di, commands
    mov cx, 6
    rep movsw

.assisted:
    mov dx, THRUST + 2
    mov si, commands
    mov cx, 6
.write:
    lodsw
    out dx, ax
    add dx, 2
    loop .write
    mov al, [assist]
    mov dx, THRUST + 15
    out dx, al
    mov dx, THRUST + 14             ; take them
    out dx, al

    dec byte [screen_wait]
    jnz .timed
    mov byte [screen_wait], SCREEN_EVERY
    call draw_values
    mov word [busy], 0
    mov word [busy + 2], 0
.timed:
    call timer_read                 ; the timer counts down
    mov bx, [t_start]
    sub bx, ax
    add [busy], bx
    adc word [busy + 2], 0
    ret

; Per axis: input, full-scale setpoint, measurement, gain (0 ends)
axes:
    dw controls + 0, MAX_PITCH,  motion + 0,  GAIN_RATE
    dw controls + 2, MAX_ROLL,   motion + 2,  GAIN_RATE
    dw controls + 4, MAX_YAW,    motion + 4,  GAIN_RATE
    dw controls + 6, MAX_STRAFE, motion + 6,  GAIN_SPEED
    dw controls + 8, MAX_STRAFE, motion + 8,  GAIN_SPEED
    dw controls + 10, MAX_SPEED, motion + 10, GAIN_SPEED
    dw 0

; CX words from the ports at DX, DX+2, ... to DI
read_words:
    in ax, dx
    stosw
    add dx, 2
    loop read_words
    ret

; AX = AX * CX / 32768
scale:
    imul cx
    shl ax, 1
    rcl dx, 1
    mov ax, dx
    ret

; AX = AX * CX, held to -32767..32767
times_clamped:
    imul cx
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

; ======================================================================
; CPU load: PIT channel 2 counting freely at 1.19 MHz
; ======================================================================

timer_start:
    in al, 61h                      ; gate on, speaker off
    and al, 0FCh
    or al, 01h
    out 61h, al
    mov al, 0B4h                    ; channel 2, mode 2, binary
    out 43h, al
    xor al, al                      ; count 65536
    out 42h, al
    out 42h, al
    ret

timer_stop:
    in al, 61h
    and al, 0FCh
    out 61h, al
    ret

timer_read:
    mov al, 80h                     ; latch channel 2
    out 43h, al
    in al, 42h
    mov ah, al
    in al, 42h
    xchg al, ah
    ret

; ======================================================================
; The screen (80 x 25, written straight into video memory)
; ======================================================================

draw_labels:
    push es
    mov ax, 0B800h
    mov es, ax
    mov si, labels
.next:
    lodsw
    cmp ax, 0FFFFh
    je .done
    mov di, ax
    lodsb
    mov ah, al
.char:
    lodsb
    test al, al
    jz .next
    stosw
    jmp .char
.done:
    pop es
    ret

draw_values:
    push es
    mov ax, 0B800h
    mov es, ax

    mov si, s_on                    ; flight assist
    mov ah, ATTR_ON
    cmp byte [assist], 0
    jne .shown
    mov si, s_off
    mov ah, ATTR_OFF
.shown:
    mov di, (3 * COLS + 16) * 2
    call put_text

    mov ax, [controls + 10]         ; throttle, percent
    mov cx, 100
    call scale
    mov di, (5 * COLS + 12) * 2
    call put_number
    mov ax, [motion + 10]           ; speeds, m/s
    mov di, (6 * COLS + 12) * 2
    call put_speed
    mov ax, [motion + 6]
    mov di, (7 * COLS + 12) * 2
    call put_speed
    mov ax, [motion + 8]
    mov di, (8 * COLS + 12) * 2
    call put_speed

    mov ax, [motion + 0]            ; turn rates, degrees a second
    mov di, (10 * COLS + 12) * 2
    call put_rate
    mov ax, [motion + 2]
    mov di, (11 * COLS + 12) * 2
    call put_rate
    mov ax, [motion + 4]
    mov di, (12 * COLS + 12) * 2
    call put_rate

    mov ax, [busy]                  ; CPU load over the last steps:
    mov dx, [busy + 2]              ; counts / (19886 * SCREEN_EVERY) * 100
    mov cx, 1193
    cmp dx, cx
    jae .full
    div cx
    jmp .load
.full:
    mov ax, 100
.load:
    mov di, (14 * COLS + 12) * 2
    call put_number
    pop es
    ret

put_speed:                          ; 1/8 m/s
    sar ax, 1
    sar ax, 1
    sar ax, 1
    jmp put_number

put_rate:                           ; 1/1024 rad/s to degrees a second
    mov cx, 3667
    imul cx
    mov ax, dx
    ; fall through

; AX signed, ES:DI: "+1234", right-aligned in 5 columns
put_number:
    mov bl, '+'
    test ax, ax
    jns .positive
    neg ax
    mov bl, '-'
.positive:
    push ax
    mov ax, (ATTR_VALUE << 8) | ' '
    mov cx, 5
    push di
    rep stosw
    pop di
    pop ax
    add di, 8
    mov cx, 10
.digit:
    xor dx, dx
    div cx
    add dl, '0'
    mov [es:di], dl
    mov byte [es:di + 1], ATTR_VALUE
    sub di, 2
    test ax, ax
    jnz .digit
    mov [es:di], bl
    mov byte [es:di + 1], ATTR_VALUE
    ret

; DS:SI ASCIIZ with attribute AH, at ES:DI
put_text:
    lodsb
    test al, al
    jz .done
    stosw
    jmp put_text
.done:
    ret

; ======================================================================
; Data
; ======================================================================

; Screen offset, attribute, text (0); 0FFFFh ends
labels:
    dw (1 * COLS + 0) * 2
    db ATTR_TITLE, ' CUCSICORP FLIGHT CONTROL 1.0           ', 0
    dw (3 * COLS + 1) * 2
    db ATTR_LABEL, 'FLIGHT ASSIST', 0
    dw (5 * COLS + 1) * 2
    db ATTR_LABEL, 'THROTTLE', 0
    dw (5 * COLS + 18) * 2
    db ATTR_LABEL, '%', 0
    dw (6 * COLS + 1) * 2
    db ATTR_LABEL, 'FORWARD', 0
    dw (6 * COLS + 18) * 2
    db ATTR_LABEL, 'm/s', 0
    dw (7 * COLS + 1) * 2
    db ATTR_LABEL, 'RIGHT', 0
    dw (7 * COLS + 18) * 2
    db ATTR_LABEL, 'm/s', 0
    dw (8 * COLS + 1) * 2
    db ATTR_LABEL, 'UP', 0
    dw (8 * COLS + 18) * 2
    db ATTR_LABEL, 'm/s', 0
    dw (10 * COLS + 1) * 2
    db ATTR_LABEL, 'PITCH', 0
    dw (10 * COLS + 18) * 2
    db ATTR_LABEL, 'deg/s', 0
    dw (11 * COLS + 1) * 2
    db ATTR_LABEL, 'ROLL', 0
    dw (11 * COLS + 18) * 2
    db ATTR_LABEL, 'deg/s', 0
    dw (12 * COLS + 1) * 2
    db ATTR_LABEL, 'YAW', 0
    dw (12 * COLS + 18) * 2
    db ATTR_LABEL, 'deg/s', 0
    dw (14 * COLS + 1) * 2
    db ATTR_LABEL, 'CPU LOAD', 0
    dw (14 * COLS + 18) * 2
    db ATTR_LABEL, '%', 0
    dw (23 * COLS + 1) * 2
    db ATTR_LABEL, 'ESC: STOP FLIGHT CONTROL', 0
    dw 0FFFFh

s_on            db 'ON ', 0
s_off           db 'OFF', 0
msg_stopped     db 'Flight control stopped: the thrusters', 13, 10
                db 'answer the controls directly.', 13, 10, '$'
msg_no_device   db 'FLIGHT: the controls, the inertial unit', 13, 10
                db 'or the thrusters are not on the bus.', 13, 10, '$'

assist          db 1
tick            db 0
buttons         db 0
screen_wait     db 1
old_irq         dw 0, 0
t_start         dw 0
busy            dw 0, 0
controls        times 6 dw 0        ; pitch, roll, yaw, right, up, throttle
motion          times 6 dw 0        ; the same axes, as measured
commands        times 6 dw 0
