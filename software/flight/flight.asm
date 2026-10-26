; flight.asm - CucsiCorp Flight Control 1.5 (FLIGHT.COM)
;
;   FLIGHT          stays resident and flies the ship
;   FLIGHT OFF      takes it out of memory: the thrusters answer the
;                   controls directly
;
; Sixty times a second the inertial unit interrupts (IRQ 5) with a new
; sample; the resident part reads it and the pilot's controls, works out
; what every group of thrusters must do, and writes the commands. It all
; happens inside the interrupt, so the console stays free for other work.
; Its page goes on the ship's FLIGHT screen (40 x 16 text), if it has one.
;
; Flight assist on: the stick asks for a turn rate and the throttle for a
; forward speed; the strafe controls ask for a sideways speed. Let go and
; the ship stops turning and drifting. Full stick and full throttle ask for
; the most the ship can do, as its rating plate says (its thrusters and
; engine against the veil's drag); the push each axis needs to hold what is
; asked against the drag goes out at once, and the error drives the rest. Flight assist off: the stick and the
; throttle drive the thrusters directly, and the ship keeps whatever motion
; it has. The pilot's FA button switches between the two.
;
; Safe turns (the pilot's SAFE button, with flight assist on): full stick
; asks only for the pitch and yaw rates the ship can bend its path to at
; its speed, so it goes where it points instead of sliding on: the rate
; the thrusters across give, (acceleration across) / (forward speed), from
; the rating plate, and more while the nose leads the path by less than
; SLIP_WINDOW (measured: the speed across against the speed ahead). A
; little slide lets the main engine and the veil bend the path too, as
; much as this ship's have it in them. The faster the ship, the gentler
; its turns; better thrusters turn it harder. Without a rating plate on
; the bus there are no safe turns.
;
; The pilot's boost button, held, drives the main engine's cooling harder
; (the cooling unit's boost): the engine keeps up more thrust before its
; controller cuts it out, and its radiators glow brighter.
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
;                            +15 more buttons: bit 0 SAFE
;   110h  inertial unit      +0 type 2   +2 pitch rate  +4 roll rate
;                            +6 yaw rate (1/1024 rad/s)  +8 right  +10 up
;                            +12 forward speed (1/8 m/s)  +14 status:
;                            reading it acknowledges the sample
;   120h  thrusters          +0 type 3   +2 pitch  +4 roll  +6 yaw torque
;                            +8 right  +10 up  +12 forward force
;                            (-32767..32767 of the most they give)
;                            +14 write: take the commands  +15 mode shown
;                            (bit 0 assist, bit 1 safe turns)
;   150h  display card       +0 type 6   +2 segment  +4 screen table
;                            +6 screens; a screen (16 bytes): +0 name
;                            +6 mode (0 text: character and attribute
;                            cells, as on a CGA)  +7 columns  +8 rows
;                            +14 memory (offset)
;   1A0h  thruster ratings   +0 type 12  what the thrusters can do to the
;                            ship: +2 forward  +4 reverse  +6 sideways
;                            +8 up and down (1/8 m/s2)  +10 pitch  +12 yaw
;                            +14 roll (1/1024 rad/s2); +1 version: 2 and
;                            the whole plate is at D000:1400 too, with
;                            the top speeds (+14 forward, +16 reverse, +18
;                            sideways, +20 up and down, 1/8 m/s) and top
;                            turn rates (+22 pitch, +24 yaw, +26 roll,
;                            1/1024 rad/s)
;   1F0h  main engine        +0 type 17  +6 status (bit 4 the cooling
;                            unit's calibration mode); write: the cooling
;                            unit, bit 0 boost, bit 7 calibration mode

org 100h

CTRL        equ 100h
IMU         equ 110h
THRUST      equ 120h
DISPLAYS    equ 150h
RATINGS     equ 1A0h
ENGINE      equ 1F0h
TYPE_CTRL   equ 1
TYPE_IMU    equ 2
TYPE_THRUST equ 3
TYPE_DISPLAYS equ 6
TYPE_RATINGS equ 12
TYPE_ENGINE equ 17
IRQ_VECTOR  equ 0Dh                 ; IRQ 5
IRQ_BIT     equ 20h

PLATE_SEG   equ 0D000h              ; the whole rating plate (version 2)
PLATE       equ 1400h
GAIN_RATE   equ 96                  ; command for each unit of turn-rate error
GAIN_SPEED  equ 204                 ; command for each unit of speed error
SAFE_SHARE  equ 1024                ; safe turns use all the push across (x 1024)
SLIP_WINDOW equ 286                 ; and let the nose lead the path by up to 16 degrees (1/1024 rad)
SAFE_SLOW   equ 16                  ; below 2 m/s forward a turn bends nothing: no limit

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
signature   db 'XPFL15'             ; 103h
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
    mov dx, CTRL + 15               ; SAFE on the press of its button
    in al, dx
    mov ah, [buttons2]
    mov [buttons2], al
    not ah
    and al, ah
    test al, 1
    jz .no_safe
    mov al, [rated]
    and al, 1
    xor [safe], al                  ; (no rating plate: nothing to go by)
.no_safe:
    cmp byte [has_engine], 0
    je .no_engine
    mov al, [buttons]               ; the boost button held: the cooling boost
    and al, 2
    shr al, 1
    cmp al, [boost]
    je .no_engine
    mov [boost], al
    mov dx, ENGINE + 6              ; the calibration mode left as it is
    in al, dx
    and al, 10h
    mov cl, 3
    shl al, cl
    or al, [boost]
    out dx, al
.no_engine:
    mov dx, IMU + 2
    mov di, motion
    mov cx, 6
    call read_words
    cmp byte [plate2], 0
    je .limits
    call read_plate
.limits:
    call turn_limits

    cmp byte [assist], 0
    je .direct

    ; flight assist: each axis holds its setpoint against the veil and
    ; drives its error to zero
    mov si, axes
    mov di, commands
.axis:
    lodsw                           ; the input, as an offset
    test ax, ax
    jz .assisted
    mov bx, ax
    lodsw                           ; ... how much full input asks for (where it is kept)
    xchg ax, bx
    mov cx, [bx]
    mov bx, ax
    mov ax, [bx]
    call scale                      ; AX = the setpoint
    mov [setpoint], ax
    lodsw                           ; ... what the ship is doing
    mov bx, ax
    mov ax, [setpoint]
    sub ax, [bx]
    mov cx, [si]                    ; ... the gain
    call times_clamped
    mov [p_term], ax
    mov bx, [si + 2]                ; ... the top, for the push that holds the setpoint
    add si, 4
    mov cx, [bx]
    mov ax, [setpoint]
    call feedforward
    mov bx, [p_term]
    add ax, bx
    jno .summed
    mov ax, 32767                   ; it went over: the most, either way
    test bx, bx
    jns .summed
    mov ax, -32767
.summed:
    cmp ax, 8000h
    jne .stored
    inc ax
.stored:
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
    mov al, [safe]
    shl al, 1
    or al, [assist]
    mov dx, THRUST + 15
    out dx, al
    mov dx, THRUST + 14             ; take them
    out dx, al

    dec byte [screen_wait]
    jnz .timed
    mov byte [screen_wait], SCREEN_EVERY
    cmp word [screen_seg], 0
    je .unseen
    call draw_values
.unseen:
    mov word [busy], 0
    mov word [busy + 2], 0
.timed:
    call timer_read                 ; the timer counts down
    mov bx, [t_start]
    sub bx, ax
    add [busy], bx
    adc word [busy + 2], 0
    ret

; Full stick's pitch and yaw rates: the most, or with safe turns what the
; push across allows at this speed (written into the axes below)
turn_limits:
    mov ax, [max_pitch]
    mov bx, [max_yaw]
    cmp byte [safe], 0
    je .set
    mov dx, RATINGS + 8             ; up and down: pitch bends the path
    in ax, dx
    mov cx, [max_pitch]
    call safe_rate
    mov bx, [motion + 8]            ; the slide up and down
    mov cx, [max_pitch]
    call slip_room
    push ax
    mov dx, RATINGS + 6             ; sideways: yaw bends it
    in ax, dx
    mov cx, [max_yaw]
    call safe_rate
    mov bx, [motion + 6]            ; the slide sideways
    mov cx, [max_yaw]
    call slip_room
    mov bx, ax
    pop ax
.set:
    mov [lim_pitch], ax
    mov [lim_yaw], bx
    ret

; The tops from the whole rating plate, every step (a worn engine, a new
; fit: it says what the ship can do now)
read_plate:
    push ds
    mov ax, PLATE_SEG
    mov ds, ax
    mov si, PLATE + 14
    mov ax, [si]                    ; forward
    mov bx, [si + 4]                ; sideways
    mov cx, [si + 6]                ; up and down
    mov dx, [si + 8]                ; pitch
    mov di, [si + 10]               ; yaw
    mov bp, [si + 12]               ; roll
    pop ds
    mov [max_speed], ax
    mov [max_side], bx
    mov [max_vert], cx
    mov [max_pitch], dx
    mov [max_yaw], di
    mov [max_roll], bp
    ret

; AX = a setpoint, CX = the top that way: AX = the push that holds it
; against the veil, (setpoint / top)^2 of the most, with its sign (none
; without the whole plate: nothing to go by)
feedforward:
    cmp byte [plate2], 0
    je .none
    jcxz .none
    mov bx, ax
    test ax, ax
    jns .positive
    neg ax
.positive:
    cmp ax, cx
    jbe .within
    mov ax, cx
.within:
    push bx
    mov bx, 32767
    mul bx
    div cx                          ; AX = setpoint / top, of 32767
    mul ax
    div bx                          ; AX = its square, of 32767
    pop bx
    test bx, bx
    jns .done
    neg ax
.done:
    ret
.none:
    xor ax, ax
    ret

; AX = an acceleration across (1/8 m/s2), CX = the most: AX = the turn rate
; (1/1024 rad/s) that bends the path as fast at the forward speed
safe_rate:
    mov bx, [motion + 10]
    test bx, bx
    jns .forward
    neg bx
.forward:
    cmp bx, SAFE_SLOW
    jb .most
    push cx
    mov cx, SAFE_SHARE
    mul cx                          ; DX:AX = share x acceleration
    pop cx
    cmp dx, bx
    jae .most                       ; it would not fit: more than the most
    div bx
    cmp ax, cx
    jbe .done
.most:
    mov ax, cx
.done:
    ret

; AX = the safe rate so far, BX = the speed across (1/8 m/s), CX = the
; most: AX = that plus what the slip window still has room for, the most
; times (window - slip) / window, held to the most
slip_room:
    push ax
    mov ax, bx
    test ax, ax
    jns .across
    neg ax
.across:
    mov bx, [motion + 10]
    test bx, bx
    jns .ahead
    neg bx
.ahead:
    cmp bx, SAFE_SLOW
    jb .none                        ; slow: no limit anyway
    push cx
    mov cx, 1024
    mul cx                          ; DX:AX = across x 1024
    pop cx
    cmp dx, bx
    jae .none                       ; sliding more than ahead: no room
    div bx                          ; AX = the slip (1/1024 rad)
    cmp ax, SLIP_WINDOW
    jae .none
    mov bx, SLIP_WINDOW
    sub bx, ax
    mov ax, cx
    mul bx
    mov bx, SLIP_WINDOW
    div bx                          ; AX = the most x room / window
    pop bx
    add ax, bx
    jc .most
    cmp ax, cx
    jbe .done
.most:
    mov ax, cx
.done:
    ret
.none:
    pop ax
    ret

; Per axis: input, where its full-scale setpoint is kept, measurement,
; gain, where its top is kept (0 ends)
axes:
    dw controls + 0, lim_pitch,  motion + 0,  GAIN_RATE,  max_pitch
    dw controls + 2, max_roll,   motion + 2,  GAIN_RATE,  max_roll
    dw controls + 4, lim_yaw,    motion + 4,  GAIN_RATE,  max_yaw
    dw controls + 6, max_side,   motion + 6,  GAIN_SPEED, max_side
    dw controls + 8, max_vert,   motion + 8,  GAIN_SPEED, max_vert
    dw controls + 10, max_speed, motion + 10, GAIN_SPEED, max_speed
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

; ---------- the page on the FLIGHT screen ----------

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

    mov si, s_on                    ; safe turns
    mov ah, ATTR_ON
    cmp byte [safe], 0
    jne .safe_shown
    mov si, s_off
    mov ah, ATTR_OFF
    cmp byte [rated], 0
    jne .safe_shown
    mov si, s_none
.safe_shown:
    mov di, (3 * COLS + 14) * 2
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

    mov ax, [axes + 2]              ; what full stick asks for in pitch
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
s_none          db '---', 0

assist          db 1
safe            db 0
rated           db 0                ; the ship has a thruster rating plate
has_engine      db 0                ; the ship has a main engine with a cooling unit
plate2          db 0                ; the rating plate gives the tops (version 2)
; What full stick and full throttle ask for: the rating plate's tops
; (these only on a ship whose plate does not give them)
max_pitch       dw 1229             ; 1/1024 rad/s
max_roll        dw 2560
max_yaw         dw 614
max_side        dw 800              ; 1/8 m/s
max_vert        dw 800
max_speed       dw 2000
lim_pitch       dw 1229             ; full stick's pitch and yaw now (safe turns)
lim_yaw         dw 614
setpoint        dw 0
p_term          dw 0
boost           db 0                ; the boost asked for, last step
buttons         db 0
buttons2        db 0
screen_wait     db 1
screen_seg      dw 0                ; the FLIGHT screen's segment (0: none)
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
    mov dx, RATINGS                 ; safe turns need the rating plate
    in al, dx
    cmp al, TYPE_RATINGS
    jne .unrated
    mov byte [rated], 1
    mov dx, RATINGS + 1             ; the whole plate, with the tops?
    in al, dx
    cmp al, 2
    jb .unrated
    mov byte [plate2], 1
    call read_plate
.unrated:
    mov dx, ENGINE                  ; a main engine to cool harder on boost
    in al, dx
    cmp al, TYPE_ENGINE
    jne .no_engine
    mov byte [has_engine], 1
.no_engine:

    mov si, n_flight                ; its page, if the ship has the screen
    call find_screen
    jc .no_page
    cmp byte [es:bx + 7], COLS
    jne .no_page
    cmp byte [es:bx + 8], 16
    jb .no_page
    mov byte [es:bx + 6], 0         ; text
    mov ax, [es:bx + 14]
    mov cl, 4
    shr ax, cl
    mov cx, es
    add ax, cx
    mov [screen_seg], ax
    call draw_labels
.no_page:

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
    call unlink                     ; out of the IRQ 5 chain, wherever it is
    cmp word [es:screen_seg], 0
    je .no_page
    push es                         ; the page says so
    mov es, [es:screen_seg]
    mov si, s_removed
    mov di, (2 * COLS + 14) * 2
    mov ah, ATTR_OFF
    call put_text
    pop es
.no_page:
    mov ah, 49h                     ; and the memory is free again
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

n_flight        db 'FLIGHT'

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
    db ATTR_TITLE, ' FLIGHT CONTROL 1.2            CUCSICORP', 0
    dw (2 * COLS + 1) * 2
    db ATTR_LABEL, 'ASSIST', 0
    dw (3 * COLS + 1) * 2
    db ATTR_LABEL, 'SAFE TURNS', 0
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
    dw (12 * COLS + 1) * 2
    db ATTR_LABEL, 'PITCH MAX', 0
    dw (12 * COLS + 18) * 2
    db ATTR_LABEL, 'deg/s', 0
    dw (13 * COLS + 1) * 2
    db ATTR_LABEL, 'CPU LOAD', 0
    dw (13 * COLS + 18) * 2
    db ATTR_LABEL, '%', 0
    dw (15 * COLS + 1) * 2
    db ATTR_LABEL, 'FLIGHT OFF: REMOVE', 0
    dw 0FFFFh

s_removed       db 'REMOVED', 0
msg_installed   db 'Flight control resident: page on the FLIGHT screen.', 13, 10
                db 'FLIGHT OFF removes it.', 13, 10, '$'
msg_already     db 'Flight control is already resident.', 13, 10, '$'
msg_not_running db 'Flight control is not resident, or a program that is not', 13, 10
                db "one of the ship's took IRQ 5 after it.", 13, 10, '$'
msg_removed     db 'Flight control removed: the thrusters answer', 13, 10
                db 'the controls directly.', 13, 10, '$'
msg_no_device   db 'FLIGHT: the controls, the inertial unit, the', 13, 10
                db 'thrusters or the side screens are not on the bus.', 13, 10, '$'
