; flight.asm - CucsiCorp Flight Control 1.7 (FLIGHT.COM)
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
; Parking (the pilot's PARK button, with flight assist on and a radio on
; the bus): full throttle and full strafe ask only for the speed limit of
; the station whose zone the ship is in, as the laws its radio holds say;
; the whole range of the controls for that much less. If the radio holds
; no laws for where the ship is, it asks the nearest station for them once
; a second, and there is no limit until they come.
;
; Docking: while the radio hears the beacon of the landing pad the ship is
; cleared to, the RADAR screen is taken for the pad (its entry marked as
; lent, so the program that draws there leaves it alone, and given back
; when the beacon is no longer heard). The pad from above, the ship in the
; middle pointing up, the pad's square as big as its leeway (half bright,
; bright when the ship is over it and turned as it is), ticks along the
; frame every metre within 14 m, every 10 m within 56 m, every 100 m
; beyond; along the bottom how far to yaw, along the top how far to roll,
; up the left how far to pitch (the middle tick: none; the others: the
; pad's leeway); up the right the height above the ship's place on the
; pad (the upper tick: where the clamps take it). Held by the clamps, a
; box round the ship; docked, a second one.
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
;                            +15 more buttons: bit 0 SAFE, bit 7 PARK
;   110h  inertial unit      +0 type 2   +2 pitch rate  +4 roll rate
;                            +6 yaw rate (1/1024 rad/s)  +8 right  +10 up
;                            +12 forward speed (1/8 m/s)  +14 status:
;                            reading it acknowledges the sample
;   120h  thrusters          +0 type 3   +2 pitch  +4 roll  +6 yaw torque
;                            +8 right  +10 up  +12 forward force
;                            (-32767..32767 of the most they give)
;                            +14 write: take the commands  +15 mode shown
;                            (bit 0 assist, bit 1 safe turns, bit 2
;                            parking, bit 3 keeping to a speed limit)
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
;   200h  radio              +0 type 18  +1 version  +4 status (bit 0
;                            it holds a station's laws, bit 1 the ship is
;                            inside their zone)  +5 write: 1 ask for the
;                            laws of the contact in +6 (FFh the nearest
;                            station); the laws at D000:1600: +8 speed
;                            limit (1/8 m/s); version 3: the landing pad's
;                            guidance at D000:1900: +0 flags (bit 0 heard,
;                            1 over it and turned as it is, 3 held, 4
;                            docked)  +2 right  +4 up  +6 forward to the
;                            ship's place on it (1/16 m)  +8 yaw  +10 pitch
;                            +12 roll to turn (binary angles)  +14 leeway
;                            (1/16 m)  +16 leeway in heading  +18 the
;                            clamps' height (1/16 m)
;
; The display card's screens (version 4): +9 lent, 0 or the mark of the
; program that has taken the screen for a while.

org 100h

CTRL        equ 100h
IMU         equ 110h
THRUST      equ 120h
DISPLAYS    equ 150h
RATINGS     equ 1A0h
ENGINE      equ 1F0h
RADIO       equ 200h
TYPE_CTRL   equ 1
TYPE_IMU    equ 2
TYPE_THRUST equ 3
TYPE_DISPLAYS equ 6
TYPE_RATINGS equ 12
TYPE_ENGINE equ 17
TYPE_RADIO  equ 18
IRQ_VECTOR  equ 0Dh                 ; IRQ 5
IRQ_BIT     equ 20h

PLATE_SEG   equ 0D000h              ; the whole rating plate (version 2)
PLATE       equ 1400h
LAWS        equ 1600h               ; the laws the radio holds (same segment)
PAD         equ 1900h               ; the landing pad's guidance (same segment)
PAD_WORDS   equ 10
LENT_MARK   equ 'F'                 ; on a screen this program has taken
MARK_WORDS  equ 8
PLAN_X0     equ 16                  ; the pad's picture on RADAR (160 x 128)
PLAN_X1     equ 143
PLAN_Y0     equ 8
PLAN_Y1     equ 119
MID_X       equ 80
MID_Y       equ 64
ROLL_Y      equ 3                   ; the bars
YAW_Y       equ 124
PITCH_X     equ 5
HEIGHT_X    equ 152
CLAMP_TICK  equ 28                  ; the clamps' height, pixels up the height bar
DIM         equ 55h                 ; 2-bit pixels, four to a byte
HALF        equ 0AAh
BRIGHT      equ 0FFh
BUTTON_PARK equ 80h                 ; on CTRL + 15
ASK_EVERY   equ 60                  ; steps between asks for the laws (a second)
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
signature   db 'XPFL17'             ; 103h
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
    test al, BUTTON_PARK
    jz .no_park
    mov ah, [has_radio]
    xor [parking], ah               ; (no radio: no laws to keep to)
.no_park:
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
    call speed_limits

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
    mov al, [parking]
    cmp word [park_limit], 0
    je .mode
    or al, 2
.mode:
    shl al, 1
    or al, [safe]
    shl al, 1
    or al, [assist]
    mov dx, THRUST + 15
    out dx, al
    mov dx, THRUST + 14             ; take them
    out dx, al

    dec byte [screen_wait]
    jnz .timed
    mov byte [screen_wait], SCREEN_EVERY
    call guide
    push cs
    pop es
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

; Full throttle's and full strafe's speeds: the tops, or parking with the
; laws of where the ship is, no more than their speed limit (written into
; the axes below). Parking without them: the radio asks now and then.
speed_limits:
    xor ax, ax                      ; the limit (0: none)
    cmp byte [parking], 0
    je .set
    mov dx, RADIO + 4
    in al, dx
    and al, 3
    cmp al, 3                       ; laws, and inside their zone?
    je .lawful
    dec byte [ask_wait]
    jnz .none
    mov byte [ask_wait], ASK_EVERY
    mov dx, RADIO + 6               ; the nearest station: its laws, please
    mov al, 0FFh
    out dx, al
    dec dx
    mov al, 1
    out dx, al
.none:
    xor ax, ax
    jmp .set
.lawful:
    mov byte [ask_wait], 1          ; (lost them: ask at once)
    push ds
    mov ax, PLATE_SEG
    mov ds, ax
    mov ax, [LAWS + 8]
    pop ds
    test ax, ax
    jnz .set
    inc ax                          ; a limit of nothing: still a limit
.set:
    mov [park_limit], ax
    mov si, max_side                ; sideways, up and down, forward
    mov di, lim_side
    mov cx, 3
.axis:
    lodsw
    mov bx, [park_limit]
    test bx, bx
    jz .keep
    cmp ax, bx
    jbe .keep
    mov ax, bx
.keep:
    stosw
    loop .axis
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
    dw controls + 6, lim_side,   motion + 6,  GAIN_SPEED, max_side
    dw controls + 8, lim_vert,   motion + 8,  GAIN_SPEED, max_vert
    dw controls + 10, lim_speed, motion + 10, GAIN_SPEED, max_speed
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

; ---------- the landing pad's guidance on the RADAR screen ----------

guide:
    cmp word [g_seg], 0
    je .done
    push ds                         ; the guidance, as the radio has it
    mov ax, PLATE_SEG
    mov ds, ax
    mov si, PAD
    mov di, pad
    mov cx, PAD_WORDS
    rep movsw
    pop ds
    test byte [pad], 1
    jnz .show
    cmp byte [g_lent], 0            ; no beacon: the screen given back
    je .done
    mov byte [g_lent], 0
    les bx, [g_entry]
    mov byte [es:bx + 9], 0
.done:
    ret
.show:
    call guide_marks                ; AL = the picture's state
    mov bl, al
    mov si, n_marks                 ; have the marks moved?
    mov di, m_marks
    mov cx, MARK_WORDS
    repe cmpsw
    mov bh, 0
    je .same
    mov bh, 1
.same:
    les di, [g_entry]               ; taken
    mov byte [es:di + 9], LENT_MARK
    mov byte [es:di + 6], 1         ; graphics
    mov es, [g_seg]
    cmp byte [g_lent], 0
    je .redraw
    cmp bl, [g_state]
    jne .redraw
    test bh, bh
    jz .done
    call xor_marks                  ; the old marks off, the new on
    call take_marks
    jmp xor_marks
.redraw:
    mov [g_state], bl
    mov byte [g_lent], 1
    call take_marks
    call background
    jmp xor_marks

; The marks' places from the guidance (n_marks); AL = the state of what
; stays drawn: held, docked, the scale
guide_marks:
    mov ax, [pad + 2]               ; the scale: 4 pixels a metre within
    call absolute                   ; 14 m, 1 within 56, 1/8 beyond
    mov bx, ax
    mov ax, [pad + 6]
    call absolute
    cmp ax, bx
    jae .far
    mov ax, bx
.far:
    mov cl, 2
    xor bl, bl
    cmp ax, 224
    jbe .scaled
    mov cl, 4
    inc bl
    cmp ax, 896
    jbe .scaled
    mov cl, 7
    inc bl
.scaled:
    mov [g_shift], cl
    mov [g_scale], bl
    mov ax, [pad + 14]              ; the leeway: the pad's square
    shr ax, cl
    mov dx, 1
    mov si, 40
    call clamp
    mov bp, ax
    mov ax, [pad + 2]               ; across: right
    mov cl, [g_shift]
    sar ax, cl
    add ax, MID_X
    mov dx, PLAN_X0 + 1             ; the square kept inside the frame
    add dx, bp
    mov si, PLAN_X1 - 1
    sub si, bp
    call clamp
    sub ax, bp
    mov [n_marks + 0], ax
    mov ax, [pad + 6]               ; up the screen: forward
    mov cl, [g_shift]
    sar ax, cl
    neg ax
    add ax, MID_Y
    mov dx, PLAN_Y0 + 1
    add dx, bp
    mov si, PLAN_Y1 - 1
    sub si, bp
    call clamp
    sub ax, bp
    mov [n_marks + 2], ax
    shl bp, 1
    mov [n_marks + 4], bp
    mov ax, HALF                    ; bright when over it and turned as it is
    test byte [pad], 2
    jz .dull
    mov ax, BRIGHT
.dull:
    mov [n_marks + 6], ax
    mov ax, [pad + 8]               ; yaw
    call angle_px
    add ax, MID_X
    mov [n_marks + 8], ax
    mov ax, [pad + 12]              ; roll
    call angle_px
    add ax, MID_X
    mov [n_marks + 10], ax
    mov ax, [pad + 10]              ; pitch, up positive
    call angle_px
    mov dx, -52
    mov si, 52
    call clamp
    neg ax
    add ax, MID_Y
    mov [n_marks + 12], ax
    mov ax, [pad + 4]               ; the height above its place
    neg ax
    jns .above
    xor ax, ax
.above:
    mov cx, [pad + 18]              ; the clamps' height, CLAMP_TICK up
    test cx, cx
    jnz .height
    inc cx
.height:
    mov bx, cx
    shl bx, 1
    shl bx, 1
    cmp ax, bx
    jbe .within
    mov ax, bx
.within:
    mov dx, CLAMP_TICK
    mul dx
    div cx
    neg ax
    add ax, PLAN_Y1
    mov dx, PLAN_Y0 + 1
    mov si, PLAN_Y1 - 1
    call clamp
    mov [n_marks + 14], ax
    mov al, [pad]                   ; held, docked; the scale
    and al, 18h
    mov ah, [g_scale]
    mov cl, 5
    shl ah, cl
    or al, ah
    ret

; AX a binary angle: 2 pixels a degree, within 60
angle_px:
    cwd
    mov cx, 91
    idiv cx
    mov dx, -60
    mov si, 60
    ; fall through

; AX held within DX..SI
clamp:
    cmp ax, dx
    jge .above
    mov ax, dx
.above:
    cmp ax, si
    jle .below
    mov ax, si
.below:
    ret

absolute:
    test ax, ax
    jns .done
    neg ax
.done:
    ret

take_marks:
    mov si, n_marks
    mov di, m_marks
    mov cx, MARK_WORDS
.next:
    lodsw
    mov [di], ax
    add di, 2
    loop .next
    ret

; XOR the marks (m_marks) onto the picture (ES)
xor_marks:
    mov bl, [m_marks + 6]           ; the pad's square
    mov cx, [m_marks + 0]
    mov dx, [m_marks + 2]
    mov ax, [m_marks + 4]
    mov [box_w], ax
    mov [box_h], ax
    call xor_box
    mov bl, BRIGHT
    mov cx, [m_marks + 8]           ; yaw, along the bottom
    dec cx
    mov dx, YAW_Y - 2
    mov si, 3
    mov bp, 5
    call xor_fill
    mov cx, [m_marks + 10]          ; roll, along the top
    dec cx
    mov dx, ROLL_Y - 2
    mov si, 3
    mov bp, 5
    call xor_fill
    mov cx, PITCH_X - 2             ; pitch, up the left
    mov dx, [m_marks + 12]
    dec dx
    mov si, 5
    mov bp, 3
    call xor_fill
    mov cx, HEIGHT_X - 3            ; the height, up the right
    mov dx, [m_marks + 14]
    dec dx
    mov si, 7
    mov bp, 3
    jmp xor_fill

; What stays drawn (ES): the frame and its scale, the ship, the bars and
; their ticks, the clamps
background:
    xor di, di
    mov cx, [g_words]
    xor ax, ax
    rep stosw
    mov bl, DIM                     ; the frame
    mov cx, PLAN_X0
    mov dx, PLAN_Y0
    mov word [box_w], PLAN_X1 - PLAN_X0
    mov word [box_h], PLAN_Y1 - PLAN_Y0
    call xor_box
    mov bl, [g_scale]               ; the scale's ticks: 1 m, 10 m, 100 m
    xor bh, bh
    mov bl, [tick_steps + bx]
    mov [tick_step], bl
    mov bl, HALF
    mov ax, 0
.across:
    mov cx, MID_X
    add cx, ax
    mov dx, PLAN_Y0 + 1
    mov bp, 3
    call xor_vspan
    test ax, ax
    jz .across_next
    mov cx, MID_X
    sub cx, ax
    mov bp, 3
    call xor_vspan
.across_next:
    mov ah, 0
    add al, [tick_step]
    cmp ax, MID_X - PLAN_X0 - 1
    jb .across
    mov ax, 0
.up:
    mov cx, PLAN_X0 + 1
    mov dx, MID_Y
    add dx, ax
    mov bp, 3
    call xor_hspan
    test ax, ax
    jz .up_next
    mov dx, MID_Y
    sub dx, ax
    mov bp, 3
    call xor_hspan
.up_next:
    mov ah, 0
    add al, [tick_step]
    cmp ax, MID_Y - PLAN_Y0 - 1
    jb .up
    mov bl, BRIGHT                  ; the ship, nose up
    mov cx, MID_X
    mov dx, MID_Y - 5
    mov bp, 11
    call xor_vspan
    mov cx, MID_X - 5
    mov dx, MID_Y + 1
    mov bp, 5
    call xor_hspan
    mov cx, MID_X + 1
    mov bp, 5
    call xor_hspan
    mov ax, [pad + 16]              ; the leeway in heading, pixels
    call angle_px
    mov dx, 1
    mov si, 52
    call clamp
    mov [tick_lee], ax
    mov bl, DIM                     ; the bars
    mov cx, PLAN_X0
    mov dx, ROLL_Y
    mov bp, PLAN_X1 - PLAN_X0 + 1
    call xor_hspan
    mov dx, YAW_Y
    mov bp, PLAN_X1 - PLAN_X0 + 1
    call xor_hspan
    mov cx, PITCH_X
    mov dx, PLAN_Y0
    mov bp, PLAN_Y1 - PLAN_Y0 + 1
    call xor_vspan
    mov cx, HEIGHT_X
    mov bp, PLAN_Y1 - PLAN_Y0 + 1
    call xor_vspan
    mov bl, HALF                    ; their ticks
    mov dx, ROLL_Y
    call across_ticks
    mov dx, YAW_Y
    call across_ticks
    mov cx, PITCH_X - 3             ; pitch: none, and the leeway
    mov dx, MID_Y
    mov bp, 7
    call xor_hspan
    mov cx, PITCH_X - 2
    add dx, [tick_lee]
    mov bp, 5
    call xor_hspan
    mov dx, MID_Y
    sub dx, [tick_lee]
    mov bp, 5
    call xor_hspan
    mov cx, HEIGHT_X - 4            ; the height: its place, the clamps'
    mov dx, PLAN_Y1 - 1
    mov bp, 9
    call xor_hspan
    mov dx, PLAN_Y1 - CLAMP_TICK
    mov bp, 9
    call xor_hspan
    test byte [g_state], 8          ; held: the clamps round the ship
    jz .free
    mov bl, HALF
    mov cx, MID_X - 8
    mov dx, MID_Y - 8
    mov word [box_w], 16
    mov word [box_h], 16
    call xor_box
.free:
    test byte [g_state], 10h        ; docked
    jz .done
    mov bl, BRIGHT
    mov cx, MID_X - 10
    mov dx, MID_Y - 10
    mov word [box_w], 20
    mov word [box_h], 20
    call xor_box
.done:
    ret

; Row DX: the middle tick and the leeway's either side
across_ticks:
    mov cx, MID_X
    sub dx, 3
    mov bp, 7
    call xor_vspan
    add dx, 4
    mov cx, MID_X
    add cx, [tick_lee]
    mov bp, 5
    sub dx, 3
    call xor_vspan
    mov cx, MID_X
    sub cx, [tick_lee]
    mov bp, 5
    call xor_vspan
    add dx, 2
    ret

; CX x, DX y: DI the byte, AL the pixel's bits in it (CX, DX kept)
locate:
    push dx
    push cx
    mov ax, dx
    mul word [g_pitch]
    mov di, cx
    shr di, 1
    shr di, 1
    add di, ax
    and cl, 3
    shl cl, 1
    mov al, 0C0h
    shr al, cl
    pop cx
    pop dx
    ret

; From (CX, DX), BP pixels across, BL the value (in every pixel of the byte): XOR
xor_hspan:
    push ax
    call locate
.pixel:
    mov ah, bl
    and ah, al
    xor [es:di], ah
    ror al, 1
    ror al, 1
    jnc .same
    inc di
.same:
    dec bp
    jnz .pixel
    pop ax
    ret

; From (CX, DX), BP pixels down, BL the value: XOR
xor_vspan:
    push ax
    call locate
    and al, bl
.pixel:
    xor [es:di], al
    add di, [g_pitch]
    dec bp
    jnz .pixel
    pop ax
    ret

; From (CX, DX), SI pixels across and BP down, BL the value: XOR
xor_fill:
    push bp
    mov bp, si
    call xor_hspan
    pop bp
    inc dx
    dec bp
    jnz xor_fill
    ret

; A frame from (CX, DX), [box_w] across and [box_h] down to its far corner, BL the value: XOR
xor_box:
    mov bp, [box_w]
    inc bp
    call xor_hspan
    push dx
    add dx, [box_h]
    mov bp, [box_w]
    inc bp
    call xor_hspan
    pop dx
    mov bp, [box_h]
    dec bp
    jle .done
    inc dx
    push bp
    call xor_vspan
    pop bp
    push cx
    add cx, [box_w]
    call xor_vspan
    pop cx
    dec dx
.done:
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

    mov si, s_park_off              ; parking
    cmp byte [parking], 0
    je .park_text
    mov si, s_park_ask
    mov ax, [park_limit]
    test ax, ax
    jz .park_text
    mov di, (8 * COLS + 12) * 2
    call put_speed
    mov si, s_ms
    mov di, (8 * COLS + 17) * 2
    mov ah, ATTR_LABEL
    call put_text
    jmp .parked
.park_text:
    cmp byte [has_radio], 0
    jne .park_shown
    mov si, s_park_none
.park_shown:
    mov di, (8 * COLS + 12) * 2
    mov ah, ATTR_OFF
    call put_text
.parked:

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
s_park_off      db '  OFF     ', 0
s_park_ask      db '  NO LAWS ', 0
s_park_none     db '  ---     ', 0
s_ms            db ' m/s ', 0

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
lim_side        dw 800              ; full strafe's and throttle's speeds now (parking)
lim_vert        dw 800
lim_speed       dw 2000
parking         db 0
has_radio       db 0                ; the ship has a radio (for the laws)
ask_wait        db 1
park_limit      dw 0                ; the speed limit kept to (1/8 m/s; 0: none)
setpoint        dw 0
p_term          dw 0
boost           db 0                ; the boost asked for, last step
buttons         db 0
buttons2        db 0
screen_wait     db 1
screen_seg      dw 0                ; the FLIGHT screen's segment (0: none)
g_seg           dw 0                ; the RADAR screen's segment, for the pad (0: none)
g_entry         dw 0, 0             ; its entry in the screen table
g_pitch         dw 0                ; bytes a row
g_words         dw 0                ; words in all
g_lent          db 0                ; it is ours now
g_state         db 0                ; what stays drawn, as it was drawn
g_shift         db 0
g_scale         db 0
tick_step       db 0
tick_steps      db 4, 10, 12        ; pixels between the scale's ticks
tick_lee        dw 0
box_w           dw 0
box_h           dw 0
pad             times PAD_WORDS dw 0    ; the guidance, as the radio has it
n_marks         times MARK_WORDS dw 0   ; the square (x, y, size, value), yaw, roll, pitch, height
m_marks         times MARK_WORDS dw 0   ; the same, as drawn
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
    mov dx, RADIO                   ; a radio, for the stations' laws
    in al, dx
    cmp al, TYPE_RADIO
    jne .no_radio
    mov byte [has_radio], 1
    mov dx, RADIO + 1               ; a radio that hears landing pads
    in al, dx
    cmp al, 3
    jb .no_radio
    mov si, n_radar                 ; and a RADAR screen to show them on
    call find_screen
    jc .no_radio
    cmp word [es:bx + 10], 160
    jb .no_radio
    cmp word [es:bx + 12], 128
    jb .no_radio
    mov [g_entry], bx
    mov [g_entry + 2], es
    mov ax, [es:bx + 10]
    shr ax, 1
    shr ax, 1
    mov [g_pitch], ax
    mul word [es:bx + 12]
    shr ax, 1
    mov [g_words], ax
    mov ax, [es:bx + 14]
    mov cl, 4
    shr ax, cl
    mov cx, es
    add ax, cx
    mov [g_seg], ax
.no_radio:

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
    cmp byte [es:g_lent], 0         ; a screen it took, given back
    je .not_lent
    push es
    les bx, [es:g_entry]
    mov byte [es:bx + 9], 0
    pop es
.not_lent:
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
n_radar         db 'RADAR '

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
    db ATTR_TITLE, ' FLIGHT CONTROL 1.7            CUCSICORP', 0
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
    dw (8 * COLS + 1) * 2
    db ATTR_LABEL, 'PARKING', 0
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
