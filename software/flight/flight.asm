; flight.asm - CucsiCorp Flight Control 1.1 (FLIGHT.COM)
;
;   FLIGHT          stays resident and flies the ship
;   FLIGHT OFF      takes it out of memory: the thrusters answer the
;                   controls directly
;
; Sixty times a second the inertial unit interrupts (IRQ 5) with a new
; sample; the resident part reads it and the pilot's controls, works out
; what every group of thrusters must do, and writes the commands. It all
; happens inside the interrupt, so the console stays free for other work.
; Its page is on the left side screen.
;
; Flight assist on: the stick asks for a turn rate and the throttle for a
; forward speed; the strafe controls ask for a sideways speed. Let go and
; the ship stops turning and drifting. Flight assist off: the stick and the
; throttle drive the thrusters directly, and the ship keeps whatever motion
; it has. The pilot's FA button switches between the two.
;
; If nothing writes commands for 100 ms, the thruster controller falls back
; to direct control, as if flight assist were off for good.
;
; The devices on the ship's bus (16 ports each, words little-endian):
;
;   100h  pilot's controls   +0 type 1   +2 pitch  +4 roll  +6 yaw
;                            +8 strafe right  +10 strafe up  +12 throttle
;                            (all -32767..32767)  +14 buttons: bit 0 FA,
;                            1 boost, 2 target ahead, 3 next target, 4 fire
;   110h  inertial unit      +0 type 2   +2 pitch rate  +4 roll rate
;                            +6 yaw rate (1/1024 rad/s)  +8 right  +10 up
;                            +12 forward speed (1/8 m/s)  +14 status:
;                            reading it acknowledges the sample
;   120h  thrusters          +0 type 3   +2 pitch  +4 roll  +6 yaw torque
;                            +8 right  +10 up  +12 forward force
;                            (-32767..32767 of the most they give)
;                            +14 write: take the commands  +15 mode shown
;   150h  side screens       +0 type 6   +2 segment  +4 left  +6 right
;                            (offsets)  +8 columns  +9 rows: 40 x 16 cells
;                            of character and attribute, as on a CGA

org 100h

CTRL        equ 100h
IMU         equ 110h
THRUST      equ 120h
DISPLAYS    equ 150h
TYPE_CTRL   equ 1
TYPE_IMU    equ 2
TYPE_THRUST equ 3
TYPE_DISPLAYS equ 6
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
COLS        equ 40

    jmp start

; ======================================================================
; The resident part
; ======================================================================

; FLIGHT OFF finds the resident copy by these, at fixed places
signature   db 'XPFLT1'             ; 103h
old_irq     dw 0, 0                 ; 109h

on_irq:
    pushf                           ; the handlers before us, and the
    call far [cs:old_irq]           ; BIOS's end of interrupt
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

; ---------- one step: sample in, commands out ----------

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

; PIT channel 2, counting down freely at 1.19 MHz (set up when installed)
timer_read:
    mov al, 80h                     ; latch channel 2
    out 43h, al
    in al, 42h
    mov ah, al
    in al, 42h
    xchg al, ah
    ret

; ---------- the page on the left screen ----------

draw_values:
    mov es, [screen_seg]

    mov si, s_on                    ; flight assist
    mov ah, ATTR_ON
    cmp byte [assist], 0
    jne .shown
    mov si, s_off
    mov ah, ATTR_OFF
.shown:
    mov di, (2 * COLS + 14) * 2
    call put_text

    mov ax, [controls + 10]         ; throttle, percent
    mov cx, 100
    call scale
    mov di, (4 * COLS + 12) * 2
    call put_number
    mov ax, [motion + 10]           ; speeds, m/s
    mov di, (5 * COLS + 12) * 2
    call put_speed
    mov ax, [motion + 6]
    mov di, (6 * COLS + 12) * 2
    call put_speed
    mov ax, [motion + 8]
    mov di, (7 * COLS + 12) * 2
    call put_speed

    mov ax, [motion + 0]            ; turn rates, degrees a second
    mov di, (9 * COLS + 12) * 2
    call put_rate
    mov ax, [motion + 2]
    mov di, (10 * COLS + 12) * 2
    call put_rate
    mov ax, [motion + 4]
    mov di, (11 * COLS + 12) * 2
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
    mov di, (13 * COLS + 12) * 2
    call put_number
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

s_on            db 'ON ', 0
s_off           db 'OFF', 0

assist          db 1
buttons         db 0
screen_wait     db 1
screen_seg      dw 0                ; the left screen: its segment, offset 0
t_start         dw 0
busy            dw 0, 0
controls        times 6 dw 0        ; pitch, roll, yaw, right, up, throttle
motion          times 6 dw 0        ; the same axes, as measured
commands        times 6 dw 0

resident_end:

; ======================================================================
; Installing and removing (not kept in memory)
; ======================================================================

start:
    mov si, 81h                     ; FLIGHT OFF?
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
    mov dx, CTRL                    ; the devices must be there
    mov bl, TYPE_CTRL
    call check_device
    mov dx, IMU
    mov bl, TYPE_IMU
    call check_device
    mov dx, THRUST
    mov bl, TYPE_THRUST
    call check_device
    mov dx, DISPLAYS
    mov bl, TYPE_DISPLAYS
    call check_device

    mov dx, DISPLAYS + 2            ; the left screen: segment + offset
    in ax, dx
    mov bx, ax
    mov dx, DISPLAYS + 4
    in ax, dx
    mov cl, 4
    shr ax, cl
    add ax, bx
    mov [screen_seg], ax
    call draw_labels

    in al, 61h                      ; PIT channel 2 free-running for the
    and al, 0FCh                    ; CPU load: gate on, speaker off
    or al, 01h
    out 61h, al
    mov al, 0B4h                    ; channel 2, mode 2, binary
    out 43h, al
    xor al, al                      ; count 65536
    out 42h, al
    out 42h, al

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

    mov es, [2Ch]                   ; the environment is not needed
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
    push ds                         ; give IRQ 5 back to whoever had it
    lds dx, [es:old_irq]
    mov ax, 2500h + IRQ_VECTOR
    int 21h
    pop ds
    push es                         ; the page says so
    mov es, [es:screen_seg]
    mov si, s_removed
    mov di, (2 * COLS + 14) * 2
    mov ah, ATTR_OFF
    call put_text
    pop es
    mov ah, 49h                     ; and the memory is free again
    int 21h
    mov dx, msg_removed
    jmp quit_message

; ZF and ES = its segment if the IRQ 5 handler is the resident FLIGHT
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
    or al, 1                        ; ZF = 0
    ret

; The device at port DX must be of type BL
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

draw_labels:
    push es
    mov es, [screen_seg]
    xor di, di                      ; clear the screen
    mov ax, (ATTR_LABEL << 8) | ' '
    mov cx, COLS * 16
    rep stosw
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

; Screen offset, attribute, text (0); 0FFFFh ends
labels:
    dw (0 * COLS + 0) * 2
    db ATTR_TITLE, ' FLIGHT CONTROL 1.1            CUCSICORP', 0
    dw (2 * COLS + 1) * 2
    db ATTR_LABEL, 'ASSIST', 0
    dw (4 * COLS + 1) * 2
    db ATTR_LABEL, 'THROTTLE', 0
    dw (4 * COLS + 18) * 2
    db ATTR_LABEL, '%', 0
    dw (5 * COLS + 1) * 2
    db ATTR_LABEL, 'FORWARD', 0
    dw (5 * COLS + 18) * 2
    db ATTR_LABEL, 'm/s', 0
    dw (6 * COLS + 1) * 2
    db ATTR_LABEL, 'RIGHT', 0
    dw (6 * COLS + 18) * 2
    db ATTR_LABEL, 'm/s', 0
    dw (7 * COLS + 1) * 2
    db ATTR_LABEL, 'UP', 0
    dw (7 * COLS + 18) * 2
    db ATTR_LABEL, 'm/s', 0
    dw (9 * COLS + 1) * 2
    db ATTR_LABEL, 'PITCH', 0
    dw (9 * COLS + 18) * 2
    db ATTR_LABEL, 'deg/s', 0
    dw (10 * COLS + 1) * 2
    db ATTR_LABEL, 'ROLL', 0
    dw (10 * COLS + 18) * 2
    db ATTR_LABEL, 'deg/s', 0
    dw (11 * COLS + 1) * 2
    db ATTR_LABEL, 'YAW', 0
    dw (11 * COLS + 18) * 2
    db ATTR_LABEL, 'deg/s', 0
    dw (13 * COLS + 1) * 2
    db ATTR_LABEL, 'CPU LOAD', 0
    dw (13 * COLS + 18) * 2
    db ATTR_LABEL, '%', 0
    dw (15 * COLS + 1) * 2
    db ATTR_LABEL, 'FLIGHT OFF: REMOVE', 0
    dw 0FFFFh

s_removed       db 'REMOVED', 0
msg_installed   db 'Flight control resident: page on the left screen.', 13, 10
                db 'FLIGHT OFF removes it.', 13, 10, '$'
msg_already     db 'Flight control is already resident.', 13, 10, '$'
msg_not_running db 'Flight control is not resident, or another program took', 13, 10
                db 'IRQ 5 after it (FIRECTL): remove that one first.', 13, 10, '$'
msg_removed     db 'Flight control removed: the thrusters answer', 13, 10
                db 'the controls directly.', 13, 10, '$'
msg_no_device   db 'FLIGHT: the controls, the inertial unit, the', 13, 10
                db 'thrusters or the side screens are not on the bus.', 13, 10, '$'
