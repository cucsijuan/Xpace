; firectl.asm - CucsiCorp Fire Control 1.2 (FIRECTL.COM)
;
;   FIRECTL         stays resident and runs the radar and the turret
;   FIRECTL OFF     takes it out of memory and turns the radar off
;
; Sixty times a second, on IRQ 5 (after the handlers installed before it):
;
;   - the pilot's target-ahead button locks the radar contact nearest the
;     middle of the view; next-target locks the one after the current one;
;   - the radar measures only where the target is, with noise that grows
;     with the range (and with jamming); an alpha-beta filter smooths the
;     positions and works out the velocity from them;
;   - with a target locked, it works out where the turret must point for a
;     round to meet it: the time t a round takes to reach the target's
;     place after t seconds, |p + v t| = s t, found by going round three
;     times from t = |p| / s; then the azimuth and elevation of that point;
;   - while the fire button is held and the turret points within half a
;     degree of where it should, it fires. With no target it fires straight
;     ahead.
;
; Angles, lengths and the square roots come from CORDIC: turning a vector
; onto the x axis in 14 small steps gives its angle and (times a known
; constant) its length, with shifts and adds only.
;
; The page on the right side screen shows the radar picture from above
; (range up, bearing across), the locked target and the turret.
;
; The devices (16 ports each, words little-endian):
;
;   100h  pilot's controls   +14 buttons: bit 2 target ahead, 3 next target,
;                            4 fire
;   110h  inertial unit      +2 pitch  +4 roll  +6 yaw rate (1/1024 rad/s)
;   130h  radar              +0 type 4  +2 mode (1 on)  +3 contacts  +4 sweeps
;                            +5 locked  +6 lock id (write)  +8 locked id
;                            +10 segment  +12 contact table  +14 track (offsets)
;           a contact (16 bytes): +0 id  +2 range m  +4 azimuth  +6 elevation
;                            +8 closing speed (1/8 m/s)
;           the track: +0 id  +2 right  +4 up  +6 forward (1/4 m, from the
;                            turret)  +8 SNR (dB)  +14 range (m)
;   140h  turret             +0 type 5  +2 azimuth  +4 elevation wanted
;                            +6 +8 now (65536 to the turn)  +10 muzzle speed
;                            +12 fire (write 1)  +13 status (bit 2 too hot)
;                            +15 rounds fired
;   150h  side screens       +2 segment  +6 right screen offset (40 x 16)

org 100h

CTRL        equ 100h
IMU         equ 110h
RADAR       equ 130h
TURRET      equ 140h
DISPLAYS    equ 150h
TYPE_RADAR  equ 4
TYPE_TURRET equ 5
TYPE_DISPLAYS equ 6
IRQ_VECTOR  equ 0Dh
IRQ_BIT     equ 20h

BUTTON_AHEAD equ 4
BUTTON_NEXT equ 8
BUTTON_FIRE equ 16

FIRE_TOLERANCE equ 91               ; half a degree, in binary angle
SCOPE_TOP   equ 2                   ; the radar picture: rows 2-13, columns 1-24
SCOPE_ROWS  equ 12
SCOPE_COLS  equ 24
SCOPE_HALF  equ 10923               ; 60 degrees either side
SCOPE_RANGE equ 8000                ; metres to the top

SCREEN_EVERY equ 6
ATTR_LABEL  equ 03h
ATTR_VALUE  equ 0Fh
ATTR_TITLE  equ 1Fh
ATTR_SCOPE  equ 08h
ATTR_CONTACT equ 0Ah
ATTR_LOCKED equ 0Eh
ATTR_WARN   equ 0Ch
COLS        equ 40

    jmp start

; ======================================================================
; The resident part
; ======================================================================

signature   db 'XPFC10'             ; 103h
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

step:
    call timer_read
    mov [t_start], ax

    mov dx, CTRL + 14               ; buttons, and which were just pressed
    in al, dx
    mov ah, [buttons]
    mov [buttons], al
    not ah
    and ah, al
    mov [pressed], ah
    test ah, BUTTON_AHEAD
    jz .no_ahead
    call lock_ahead
.no_ahead:
    test byte [pressed], BUTTON_NEXT
    jz .no_next
    call lock_next
.no_next:

    mov dx, TURRET + 6              ; where the turret points now
    in ax, dx
    mov [turret_az], ax
    mov dx, TURRET + 8
    in ax, dx
    mov [turret_el], ax

    mov dx, IMU + 2                 ; the gyros, for the ship's own turning
    in ax, dx                       ; (not +14: acknowledging is FLIGHT's)
    mov [pitch_rate], ax
    mov dx, IMU + 4
    in ax, dx
    mov [roll_rate], ax
    mov dx, IMU + 6
    in ax, dx
    mov [yaw_rate], ax

    mov dx, RADAR + 5
    in al, dx
    mov [locked], al
    test al, al
    jz .ahead
    call solve                      ; aim_az, aim_el
    jmp .aim
.ahead:
    mov word [filter_id], 0         ; (the next lock starts the filter again)
    mov word [aim_az], 0            ; nothing locked: straight ahead
    mov word [aim_el], 0
.aim:
    mov ax, [aim_az]
    mov dx, TURRET + 2
    out dx, ax
    mov ax, [aim_el]
    mov dx, TURRET + 4
    out dx, ax

    mov byte [firing], 0            ; fire: the button, and on target
    test byte [buttons], BUTTON_FIRE
    jz .fired
    mov ax, [aim_az]
    sub ax, [turret_az]
    call absolute
    mov bx, ax
    mov ax, [aim_el]
    sub ax, [turret_el]
    call absolute
    add ax, bx
    cmp ax, FIRE_TOLERANCE
    ja .fired
    mov byte [firing], 1
    mov al, 1
    mov dx, TURRET + 12
    out dx, al
.fired:

    dec byte [screen_wait]
    jnz .timed
    mov byte [screen_wait], SCREEN_EVERY
    call draw
    mov word [busy], 0
    mov word [busy + 2], 0
.timed:
    call timer_read
    mov bx, [t_start]
    sub bx, ax
    add [busy], bx
    adc word [busy + 2], 0
    ret

; ---------- choosing a target ----------

; The contact with the smallest |azimuth| + |elevation|
lock_ahead:
    mov dx, RADAR + 3
    in al, dx
    xor ah, ah
    mov cx, ax
    jcxz .none
    push ds
    mov ds, [cs:radar_seg]
    mov si, [cs:table_off]
    mov bx, 0FFFFh                  ; the best so far
    xor di, di                      ; its id
.contact:
    mov ax, [si + 4]
    call absolute
    mov dx, ax
    mov ax, [si + 6]
    call absolute
    add ax, dx
    cmp ax, bx
    jae .worse
    mov bx, ax
    mov di, [si]
.worse:
    add si, 16
    loop .contact
    pop ds
    mov ax, di
    mov dx, RADAR + 6
    out dx, ax
.none:
    ret

; The contact after the locked one in the table (or the first)
lock_next:
    mov dx, RADAR + 3
    in al, dx
    xor ah, ah
    mov cx, ax
    jcxz .none
    mov dx, RADAR + 8
    in ax, dx
    mov bx, ax                      ; the locked id (0: none)
    push ds
    mov ds, [cs:radar_seg]
    mov si, [cs:table_off]
    mov di, [si]                    ; the first, if the locked one is not found
    mov dx, cx
.find:
    cmp [si], bx
    je .found
    add si, 16
    dec dx
    jnz .find
    jmp .take
.found:
    dec dx                          ; the one after it, if there is one
    jz .take
    mov di, [si + 16]
.take:
    pop ds
    mov ax, di
    mov dx, RADAR + 6
    out dx, ax
.none:
    ret

; ---------- the firing solution ----------

; From the radar's track: the filter's estimate, then aim_az, aim_el,
; lead (1/1024 s), range
solve:
    push ds
    mov ds, [cs:radar_seg]
    mov si, [cs:track_off]
    mov di, track
    mov cx, 8
    rep movsw                       ; (ES = our segment)
    pop ds
    mov ax, [track + 14]
    mov [range], ax
    mov ax, [track + 8]
    mov [snr], ax
    mov ax, [track]                 ; a new target: start the filter on it
    cmp ax, [filter_id]
    je .same
    mov [filter_id], ax
    call filter_start
    jmp .estimated
.same:
    call filter_step
.estimated:
    xor bx, bx                      ; p (1/4 m) and v (1/8 m/s) from the filter
    xor si, si
.state:
    mov ax, [pos + si]
    mov dx, [pos + si + 2]
    mov cx, 4
.quarter:
    sar dx, 1
    rcr ax, 1
    loop .quarter
    mov [p + bx], ax
    mov ax, [vel + si]
    mov dx, [vel + si + 2]
    mov cx, 3
.eighth:
    sar dx, 1
    rcr ax, 1
    loop .eighth
    mov [v + bx], ax
    add si, 4
    add bx, 2
    cmp bx, 6
    jb .state

    mov ax, [range]
    call lead_time                  ; t from the range
    mov byte [rounds_left], 3
.again:
    xor bx, bx                      ; aim = p + v t
.axis:
    mov ax, [v + bx]                ; v (1/8 m/s) * t (1/1024 s) / 2048 = 1/4 m
    imul word [lead]
    mov si, ax
    mov cl, 11
    shr si, cl
    mov cl, 5
    shl dx, cl
    or dx, si
    add dx, [p + bx]
    mov [aim + bx], dx
    add bx, 2
    cmp bx, 6
    jb .axis
    mov bx, [aim + 4]               ; azimuth: forward, right
    mov bp, [aim + 0]
    call cordic
    mov [aim_az], ax
    mov bp, [aim + 2]               ; elevation: across, up
    call cordic
    mov [aim_el], ax
    mov ax, bx                      ; distance to the aim point, in metres
    shr ax, 1
    shr ax, 1
    call lead_time
    dec byte [rounds_left]
    jnz .again
    ret

; ---------- the alpha-beta filter ----------
;
; The radar gives only noisy positions. Per axis the filter keeps a position
; x and a velocity v (1/64 m, 1/64 m/s, 32 bits) and every step:
;   turns       x and v by the ship's own rotation since the last step (the
;               gyros' rates): the ship turning makes the target sweep past
;               in the ship's axes, and that is not the target moving
;   predicts    x = x + v dt
;   compares    r = measured - x
;   corrects    x = x + ALPHA r,   v = v + (BETA / dt) r
; Small gains smooth the noise out but follow a turn late; large gains
; follow at once and pass the noise on. These are a compromise.

ALPHA       equ 31                  ; 0.12, in 1/256
BETA_DT     equ 118                 ; 0.0077 * 60, in 1/256

filter_start:                       ; x = the measurement, v = 0
    xor bx, bx
    xor si, si
.axis:
    mov ax, [track + 2 + bx]
    mov cx, 16
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

filter_step:
    mov si, p                       ; the ship's rotation: positions (1/4 m to
    mov cx, 3840                    ; 1/64 m a step: * 16 / 1024 / 60)
    call rotation
    mov bx, pos
    call add_turn
    mov si, v                       ; and velocities (1/8 m/s to 1/64 m/s
    mov cx, 7680                    ; a step: * 8 / 1024 / 60)
    call rotation
    mov bx, vel
    call add_turn

    xor bx, bx
    xor si, si
.axis:
    mov ax, [vel + si]              ; predict: x += v / 60
    mov dx, [vel + si + 2]
    mov cx, 60
    idiv cx
    cwd
    add [pos + si], ax
    adc [pos + si + 2], dx
    mov ax, [track + 2 + bx]        ; r = measured (1/4 m, so * 16) - x
    mov cx, 16
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

; How a point fixed in space moves in the ship's axes (right, up, forward)
; while the ship turns at pitch rate q (nose up), roll rate p (right wing
; down) and yaw rate r (nose right), in 1/1024 rad/s:
;   right   -= r forward + p up        up += p right - q forward
;   forward += q up + r right
; SI = the vector (3 words), CX = the divisor to the step's units:
; turn_x, turn_y, turn_z (words)
rotation:
    mov [divisor], cx
    mov ax, [si + 4]                ; right: -(r * forward) - p * up
    imul word [yaw_rate]
    mov bx, ax
    mov cx, dx
    mov ax, [si + 2]
    imul word [roll_rate]
    add ax, bx
    adc dx, cx
    neg dx                          ; minus the sum
    neg ax
    sbb dx, 0
    call divide
    mov [turn_x], ax
    mov ax, [si]                    ; up: p * right - q * forward
    imul word [roll_rate]
    mov bx, ax
    mov cx, dx
    mov ax, [si + 4]
    imul word [pitch_rate]
    sub bx, ax
    sbb cx, dx
    mov ax, bx
    mov dx, cx
    call divide
    mov [turn_y], ax
    mov ax, [si + 2]                ; forward: q * up + r * right
    imul word [pitch_rate]
    mov bx, ax
    mov cx, dx
    mov ax, [si]
    imul word [yaw_rate]
    add ax, bx
    adc dx, cx
    call divide
    mov [turn_z], ax
    ret

; AX = DX:AX / divisor, held to a word (a wild value must not fault)
divide:
    mov cx, [divisor]
    shr cx, 1                       ; the quotient fits if |DX| < divisor / 2
    cmp dx, cx
    jge .high
    neg cx
    cmp dx, cx
    jl .low
    idiv word [divisor]
    ret
.high:
    mov ax, 32767
    ret
.low:
    mov ax, -32767
    ret

; Adds turn_x, turn_y, turn_z to the three dwords at BX
add_turn:
    mov si, turn_x
    mov cx, 3
.axis:
    lodsw
    cwd
    add [bx], ax
    adc [bx + 2], dx
    add bx, 4
    loop .axis
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

; lead = AX metres * 1024 / muzzle speed (1/1024 s). Changes BX, CX, DX.
lead_time:
    mov cx, 1024
    mul cx
    mov bx, ax
    push dx
    mov dx, TURRET + 10
    in ax, dx
    mov cx, ax
    pop dx
    mov ax, bx
    jcxz .no_gun
    cmp dx, cx                      ; would not fit in a word
    jae .far
    div cx
    cmp ax, 32767
    ja .far
    mov [lead], ax
    ret
.far:
    mov word [lead], 32767
    ret
.no_gun:
    mov word [lead], 0
    ret

; ---------- CORDIC ----------

; BX = x, BP = y: AX = the angle of (x, y) (65536 to the turn, y toward
; positive), BX = its length. Changes CX, DX, SI, DI, BP.
cordic:
    xor di, di                      ; the angle turned so far
    test bx, bx
    jns .right_half
    neg bx                          ; the left half: turn it round first
    neg bp
    mov di, 32768
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
    add di, [atan_steps + si]
    jmp .stepped
.up:
    sub bx, ax
    add bp, dx
    sub di, [atan_steps + si]
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
    mov ax, di
    ret

atan_steps  dw 8192, 4836, 2555, 1297, 651, 326, 163, 81, 41, 20, 10, 5, 3, 1

absolute:
    test ax, ax
    jns .done
    neg ax
.done:
    ret

timer_read:
    mov al, 80h
    out 43h, al
    in al, 42h
    mov ah, al
    in al, 42h
    xchg al, ah
    ret

; ---------- the page ----------

draw:
    mov es, [screen_seg]
    mov di, (SCOPE_TOP * COLS + 1) * 2      ; the radar picture, blank
    mov dx, SCOPE_ROWS
.row:
    push di
    mov cx, SCOPE_COLS
    mov ax, (ATTR_SCOPE << 8) | 0FAh
    rep stosw
    pop di
    add di, COLS * 2
    dec dx
    jnz .row

    mov dx, RADAR + 3               ; the contacts on it
    in al, dx
    xor ah, ah
    mov cx, ax
    jcxz .plotted
    mov dx, RADAR + 8
    in ax, dx
    mov [locked_id], ax
    push ds
    mov ds, [cs:radar_seg]
    mov si, [cs:table_off]
.plot:
    push cx
    mov ax, [si + 4]                ; column from the bearing
    add ax, SCOPE_HALF
    jl .off
    cmp ax, 2 * SCOPE_HALF
    jg .off
    mov cx, SCOPE_COLS - 1
    mul cx
    mov cx, 2 * SCOPE_HALF
    div cx
    mov bx, ax
    mov ax, [si + 2]                ; row from the range
    cmp ax, SCOPE_RANGE
    jae .off
    mov cx, SCOPE_ROWS
    mul cx
    mov cx, SCOPE_RANGE
    div cx
    mov dx, SCOPE_TOP + SCOPE_ROWS - 1
    sub dx, ax
    mov ax, COLS
    mul dx
    add ax, bx
    inc ax
    shl ax, 1
    mov di, ax
    mov ax, (ATTR_CONTACT << 8) | '+'
    mov dx, [si]
    cmp dx, [cs:locked_id]
    jne .mark
    mov ax, (ATTR_LOCKED << 8) | 0FEh
.mark:
    stosw
.off:
    add si, 16
    pop cx
    loop .plot
    pop ds
.plotted:

    xor ax, ax                      ; the target
    cmp byte [locked], 0
    je .no_target
    mov ax, [track]
.no_target:
    mov di, (2 * COLS + 33) * 2
    call put_number
    mov ax, [range]
    cmp byte [locked], 0
    jne .has_range
    xor ax, ax
.has_range:
    mov di, (3 * COLS + 33) * 2
    call put_unsigned
    mov ax, [lead]                  ; lead time, ms (1/1024 s, near enough)
    cmp byte [locked], 0
    jne .has_lead
    xor ax, ax
.has_lead:
    mov di, (4 * COLS + 33) * 2
    call put_unsigned
    mov ax, [snr]                   ; how clearly the radar sees it
    cmp byte [locked], 0
    jne .has_snr
    xor ax, ax
.has_snr:
    mov di, (5 * COLS + 33) * 2
    call put_unsigned

    mov ax, [turret_az]             ; the turret, degrees
    mov cx, 360
    imul cx
    mov ax, dx
    mov di, (6 * COLS + 33) * 2
    call put_number
    mov ax, [turret_el]
    mov cx, 360
    imul cx
    mov ax, dx
    mov di, (7 * COLS + 33) * 2
    call put_number

    mov si, s_no_target             ; what it is doing
    mov ah, ATTR_LABEL
    mov dx, TURRET + 13
    in al, dx
    test al, 4
    jz .not_hot
    mov si, s_too_hot
    mov ah, ATTR_WARN
    jmp .status
.not_hot:
    test al, 2                      ; the target is where the turret cannot go
    jz .in_arc
    cmp byte [locked], 0
    je .in_arc
    mov si, s_out_of_arc
    mov ah, ATTR_WARN
    jmp .status
.in_arc:
    cmp byte [firing], 0
    je .not_firing
    mov si, s_firing
    mov ah, ATTR_WARN
    jmp .status
.not_firing:
    cmp byte [locked], 0
    je .status
    mov si, s_tracking
    mov ah, ATTR_LOCKED
.status:
    mov di, (9 * COLS + 27) * 2
    call put_text

    mov dx, TURRET + 15             ; rounds fired (the low byte)
    in al, dx
    xor ah, ah
    mov di, (11 * COLS + 33) * 2
    call put_unsigned

    mov ax, [busy]                  ; CPU load
    mov dx, [busy + 2]
    mov cx, 1193
    cmp dx, cx
    jae .full
    div cx
    jmp .load
.full:
    mov ax, 100
.load:
    mov di, (13 * COLS + 33) * 2
    call put_unsigned
    ret

; AX signed or unsigned, ES:DI: right-aligned in 5 columns
put_number:
    mov bl, '+'
    test ax, ax
    jns put_digits
    neg ax
    mov bl, '-'
    jmp put_digits
put_unsigned:
    mov bl, ' '
put_digits:
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

s_no_target     db 'NO TARGET  ', 0
s_tracking      db 'TRACKING   ', 0
s_firing        db 'FIRING     ', 0
s_too_hot       db 'GUN TOO HOT', 0
s_out_of_arc    db 'OUT OF ARC ', 0

buttons         db 0
pressed         db 0
locked          db 0
firing          db 0
rounds_left     db 0
screen_wait     db 1
screen_seg      dw 0
radar_seg       dw 0
table_off       dw 0
track_off       dw 0
locked_id       dw 0
turret_az       dw 0
turret_el       dw 0
aim_az          dw 0
aim_el          dw 0
lead            dw 0
range           dw 0
t_start         dw 0
busy            dw 0, 0
track           times 8 dw 0        ; id, right, up, forward (1/4 m), SNR, -, -, range
aim             times 3 dw 0        ; right, up, forward (1/4 m)
filter_id       dw 0                ; the target the filter follows
snr             dw 0
residual        dw 0
pos             times 6 dw 0        ; the filter: x per axis (1/64 m, 32 bits)
vel             times 6 dw 0        ;             v per axis (1/64 m/s, 32 bits)
pitch_rate      dw 0                ; the gyros (1/1024 rad/s)
roll_rate       dw 0
yaw_rate        dw 0
divisor         dw 0
turn_x          dw 0
turn_y          dw 0
turn_z          dw 0
p               times 3 dw 0        ; its estimate in 1/4 m
v               times 3 dw 0        ; and in 1/8 m/s

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
    mov dx, RADAR
    mov bl, TYPE_RADAR
    call check_device
    mov dx, TURRET
    mov bl, TYPE_TURRET
    call check_device
    mov dx, DISPLAYS
    mov bl, TYPE_DISPLAYS
    call check_device

    mov dx, DISPLAYS + 2            ; the right screen
    in ax, dx
    mov bx, ax
    mov dx, DISPLAYS + 6
    in ax, dx
    mov cl, 4
    shr ax, cl
    add ax, bx
    mov [screen_seg], ax
    mov dx, RADAR + 10              ; the radar's picture
    in ax, dx
    mov [radar_seg], ax
    mov dx, RADAR + 12
    in ax, dx
    mov [table_off], ax
    mov dx, RADAR + 14
    in ax, dx
    mov [track_off], ax
    call draw_labels

    mov al, 1                       ; radar on
    mov dx, RADAR + 2
    out dx, al

    in al, 61h                      ; PIT channel 2 free-running (as FLIGHT
    and al, 0FCh                    ; does), for the CPU load
    or al, 01h
    out 61h, al
    mov al, 0B4h
    out 43h, al
    xor al, al
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
    xor al, al                      ; radar off
    mov dx, RADAR + 2
    out dx, al
    push es
    mov es, [es:screen_seg]
    mov si, s_removed
    mov di, (9 * COLS + 27) * 2
    mov ah, ATTR_WARN
    call put_text
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

draw_labels:
    push es
    mov es, [screen_seg]
    xor di, di
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

labels:
    dw (0 * COLS + 0) * 2
    db ATTR_TITLE, ' FIRE CONTROL 1.2              CUCSICORP', 0
    dw (1 * COLS + 1) * 2
    db ATTR_SCOPE, '-60', 0
    dw (1 * COLS + 12) * 2
    db ATTR_SCOPE, '0', 0
    dw (1 * COLS + 22) * 2
    db ATTR_SCOPE, '+60', 0
    dw (2 * COLS + 25) * 2
    db ATTR_SCOPE, '8', 0
    dw (13 * COLS + 25) * 2
    db ATTR_SCOPE, '0', 0
    dw (2 * COLS + 27) * 2
    db ATTR_LABEL, 'TARGET', 0
    dw (3 * COLS + 27) * 2
    db ATTR_LABEL, 'RANGE', 0
    dw (3 * COLS + 38) * 2
    db ATTR_LABEL, 'm', 0
    dw (4 * COLS + 27) * 2
    db ATTR_LABEL, 'LEAD', 0
    dw (4 * COLS + 38) * 2
    db ATTR_LABEL, 'ms', 0
    dw (5 * COLS + 27) * 2
    db ATTR_LABEL, 'SNR', 0
    dw (5 * COLS + 38) * 2
    db ATTR_LABEL, 'dB', 0
    dw (6 * COLS + 27) * 2
    db ATTR_LABEL, 'TUR AZ', 0
    dw (7 * COLS + 27) * 2
    db ATTR_LABEL, 'TUR EL', 0
    dw (11 * COLS + 27) * 2
    db ATTR_LABEL, 'ROUNDS', 0
    dw (13 * COLS + 27) * 2
    db ATTR_LABEL, 'CPU', 0
    dw (13 * COLS + 38) * 2
    db ATTR_LABEL, '%', 0
    dw (15 * COLS + 1) * 2
    db ATTR_LABEL, 'T: TARGET AHEAD  G: NEXT', 0
    dw 0FFFFh

s_removed       db 'REMOVED    ', 0
msg_installed   db 'Fire control resident: page on the right screen.', 13, 10
                db 'FIRECTL OFF removes it.', 13, 10, '$'
msg_already     db 'Fire control is already resident.', 13, 10, '$'
msg_not_running db 'Fire control is not resident, or another program took', 13, 10
                db 'IRQ 5 after it: remove that one first.', 13, 10, '$'
msg_removed     db 'Fire control removed: radar off.', 13, 10, '$'
msg_no_device   db 'FIRECTL: the radar, the turret or the side screens', 13, 10
                db 'are not on the bus.', 13, 10, '$'
