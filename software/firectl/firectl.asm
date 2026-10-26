; firectl.asm - CucsiCorp Fire Control 1.12 (FIRECTL.COM)
;
;   FIRECTL         stays resident and runs the radar and the turret
;   FIRECTL OFF     takes it out of memory and turns the radar off
;
; Sixty times a second, on IRQ 5 (after the handlers installed before it):
;
;   - the pilot's target-ahead button locks the radar contact nearest the
;     middle of the view, or lets the target go if one is locked;
;     next-target locks the one after the current one;
;   - the radar measures only where the target is, with noise that grows
;     with the range (and with jamming); an alpha-beta filter smooths the
;     positions and works out the velocity from them;
;   - with a target locked, it works out where the turret must point for a
;     round to meet it: the time t a round takes to reach the target's
;     place after t seconds, |p + v t| = s t, found by going round three
;     times from t = |p| / s; then the azimuth and elevation of that point;
;   - while the fire button is held and the turret points within half a
;     degree of where it should, it fires. With no target it fires straight
;     ahead;
;   - the next-weapon button goes round the cannon, the radar missiles and
;     the heat seekers (if the ship has a launcher); with a missile
;     selected, a press of the fire button lets one go at the locked
;     target. The launcher's datalink guides a radar missile with the
;     radar's track: keep the target locked. A heat seeker needs no radar:
;     the first press powers the head of the one on the rail, which looks
;     ahead (or where the radar's target is), growls in the pilot's headset
;     when it sees heat and locks on to it (IR TONE, IR LOCK); the second
;     press lets it go, whatever the head sees. With none left there is no
;     head to power;
;   - a ship needs the radar and the turret or the laser (a ship with no
;     turret starts with the laser selected);
;   - with the pulse laser selected (if the ship has one: it is fixed along
;     the nose, the pilot aims it with the ship) the fire button held fires
;     it, and with a target locked the same solution as the turret's, for
;     the laser's bolt speed, gives the lead point: where a pulse fired now
;     meets the target. It goes to the laser's sight, and the head-up
;     display draws a circle there.
;
; Angles, lengths and the square roots come from CORDIC: turning a vector
; onto the x axis in 14 small steps gives its angle and (times a known
; constant) its length, with shifts and adds only.
;
; It draws on the ship's screens it finds by name: a radar scope on RADAR
; (graphics: bearing across, range up to 8, 6, 4, 2 or 1 km, written in
; the top left corner, a dotted line every quarter; the locked target in
; brackets; a jammer's noise as a dim line up the scope at its bearing,
; a strobe, which cannot be locked; with RADAR the screen of interest, its
; zoom keys step the range) and
; a page on WEAPON (text, 14 x 10 or more: the target, the turret and its
; heat). Without them it works the same, unseen.
;
; The devices (16 ports each, words little-endian):
;
;   100h  pilot's controls   +14 buttons: bit 2 target ahead, 3 next target,
;                            4 fire, 5 next weapon
;   110h  inertial unit      +2 pitch  +4 roll  +6 yaw rate (1/1024 rad/s)
;   130h  radar              +0 type 4  +2 mode (1 on)  +3 contacts  +4 sweeps
;                            +5 locked  +6 lock id (write)  +8 locked id
;                            +10 segment  +12 contact table  +14 track (offsets)
;           a contact (16 bytes): +0 id  +2 range m  +4 azimuth  +6 elevation
;                            +8 closing speed (1/8 m/s)  +10 flags (bit 0
;                            locked, bit 1 a jammer's strobe: no range)
;           the track: +0 id  +2 right  +4 up  +6 forward (1/4 m, from the
;                            turret)  +8 SNR (dB)  +14 range (m)
;   140h  turret             +0 type 5  +2 azimuth  +4 elevation wanted
;                            +6 +8 now (65536 to the turn)  +10 muzzle speed
;                            +12 fire (write 1)  +13 status (bit 2 too hot)
;                            +15 rounds fired
;   150h  display card       +2 segment  +4 screen table  +6 screens
;                            +8 keys pressed so far  +9 the last key (1 zoom
;                            in, 2 zoom out)  +10 its screen
;           a screen (16 bytes): +0 name  +6 mode (0 text, 1 graphics)
;                            +7 columns  +8 rows  +10 width  +12 height
;                            +14 memory (offset)
;   160h  missile launcher   +0 type 7  +2 radar missiles left  +3 in flight
;                            +4 status (bit 0 radar missile ready, 2 heat
;                            seeker ready, 3 its head sees heat, 4 locked on)
;                            +6 fire (write 1 radar, 2 heat seeker)
;                            +7 bit 0 the heat seeker's head powered
;                            the newest missile: +8 time to go (1/8 s)
;                            +10 status (bit 1 no word, 2 lost)  +11 1 flying,
;                            2 hit, 3 missed  +12 fuel (m/s)  +14 heat
;                            seekers left  +15 the newest's kind (2 heat)
;   190h  pulse laser        +0 type 11  +2 status (bit 0 ready, 2 too hot)
;                            +3 heat  +4 fire (write 1)  +6 bolt speed (m/s)
;                            +8 +10 +12 the lead point (right, up, forward,
;                            1/4 m, from where the radar measures)  +14 bit 0
;                            show the lead circle (write)

org 100h

CTRL        equ 100h
IMU         equ 110h
RADAR       equ 130h
TURRET      equ 140h
DISPLAYS    equ 150h
LAUNCHER    equ 160h
LASER       equ 190h
TYPE_RADAR  equ 4
TYPE_TURRET equ 5
TYPE_DISPLAYS equ 6
TYPE_LAUNCHER equ 7
TYPE_LASER  equ 11
IRQ_VECTOR  equ 0Dh
IRQ_BIT     equ 20h

BUTTON_AHEAD equ 4
BUTTON_NEXT equ 8
BUTTON_FIRE equ 16
BUTTON_WEAPON equ 32
WEAPONS     equ 4                   ; the cannon, radar missiles, heat seekers, the laser
WEAPON_LASER equ 3
LAUNCH_SHOWN equ 30                 ; steps 'LAUNCH' stays on the page
RESULT_SHOWN equ 180                ; and 'HIT' or 'MISSED'

FIRE_TOLERANCE equ 91               ; half a degree, in binary angle
SCOPE_HALF  equ 10923               ; 60 degrees either side
SCOPE_SCALES equ 5                  ; ranges to the top (the table 'scope_scales')
KEY_IN      equ 1                   ; the screen keys
KEY_OUT     equ 2

SCREEN_EVERY equ 6
ATTR_LABEL  equ 03h
ATTR_VALUE  equ 0Fh
ATTR_TITLE  equ 1Fh
ATTR_SCOPE  equ 08h
ATTR_CONTACT equ 0Ah
ATTR_LOCKED equ 0Eh
ATTR_WARN   equ 0Ch
MAX_MARKS   equ 16                  ; contacts drawn on the scope
VALUE_COL   equ 4                   ; the page: values in columns 4-8
HEAT_COL    equ 3                   ; the heat bar in columns 3-13
HEAT_CELLS  equ 11

    jmp start

; ======================================================================
; The resident part
; ======================================================================

signature   db 'XPFC12'             ; 103h
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
    test byte [pressed], BUTTON_WEAPON
    jz .no_weapon
    call next_weapon
.no_weapon:
    call launcher_news

    mov dx, DISPLAYS + 8            ; a screen key for RADAR since the last look?
    in al, dx
    cmp al, [keys_seen]
    je .no_key
    mov [keys_seen], al
    mov dx, DISPLAYS + 10
    in al, dx
    cmp al, [r_screen]
    jne .no_key
    mov dx, DISPLAYS + 9
    in al, dx
    call scope_key
.no_key:

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

    mov dx, TURRET + 10             ; the speed the solution is for: the
    cmp byte [weapon], WEAPON_LASER ; cannon's rounds or the laser's bolts
    jne .speed
    mov dx, LASER + 6
.speed:
    in ax, dx
    mov [muzzle], ax

    mov dx, RADAR + 5
    in al, dx
    mov [locked], al
    test al, al
    jz .ahead
    call solve                      ; aim_az, aim_el
    call laser_sight
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

    mov byte [firing], 0
    cmp byte [weapon], 0
    je .gun
    cmp byte [weapon], WEAPON_LASER
    jne .missile
    test byte [buttons], BUTTON_FIRE    ; the laser: while the button is held
    jz .fired
    mov byte [firing], 1
    mov al, 1
    mov dx, LASER + 4
    out dx, al
    jmp .fired
.missile:
    test byte [pressed], BUTTON_FIRE    ; a missile: one a press
    jz .fired
    cmp byte [weapon], 2
    jne .radar_missile
    cmp byte [head_on], 0           ; a heat seeker: the first press wakes its head
    jne .heat_ready
    call head_up
    jmp .fired
.heat_ready:
    mov ah, 4                       ; the second lets it go, when the launcher is ready
    jmp .ready
.radar_missile:
    cmp byte [locked], 0            ; a radar missile: at the lock
    je .fired
    mov ah, 1
.ready:
    mov dx, LAUNCHER + 4            ; is the launcher ready for this one?
    in al, dx
    test al, ah
    jz .fired
    mov al, [weapon]                ; 1 a radar missile, 2 a heat seeker
    mov dx, LAUNCHER + 6
    out dx, al
    mov byte [head_on], 0           ; the next one on the rail starts with its head off
    mov word [launch_wait], LAUNCH_SHOWN
    jmp .fired
.gun:
    test byte [buttons], BUTTON_FIRE    ; the gun: the button, and on target
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

; The contact with the smallest |azimuth| + |elevation|; with a target
; locked, none: the lock is let go
lock_ahead:
    mov dx, RADAR + 8
    in ax, dx
    test ax, ax
    jz .choose
    xor ax, ax
    mov dx, RADAR + 6
    out dx, ax
    ret
.choose:
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
    test byte [si + 10], 2          ; a jammer's strobe: nothing to lock
    jnz .worse
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
    test byte [si + 16 + 10], 2     ; strobes come after the echoes: back to the first
    jnz .take
    mov di, [si + 16]
.take:
    pop ds
    mov ax, di
    mov dx, RADAR + 6
    out dx, ax
.none:
    ret

; ---------- the weapons ----------

next_weapon:
    inc byte [weapon]               ; the cannon, radar missiles, heat seekers,
    cmp byte [weapon], WEAPONS      ; the laser: those the ship has
    jb .has
    mov byte [weapon], 0
    cmp byte [has_turret], 0
    je next_weapon
    jmp .chosen
.has:
    cmp byte [weapon], WEAPON_LASER
    je .laser
    cmp byte [has_launcher], 0
    je next_weapon
    jmp .chosen
.laser:
    cmp byte [has_laser], 0
    je next_weapon
.chosen:
    xor al, al                      ; the heat seeker's head off, whatever comes now
    mov [head_on], al
    mov dx, LAUNCHER + 7
    out dx, al
    cmp word [w_seg], 0
    je .done
    call draw_labels                ; the other weapon's page
    mov byte [screen_wait], 1
.done:
    ret

; The heat seeker's head on, if there is one on the rail
head_up:
    mov dx, LAUNCHER + 14
    in al, dx
    test al, al
    jz .done
    mov byte [head_on], 1
    mov al, 1
    mov dx, LAUNCHER + 7
    out dx, al
.done:
    ret

; How the newest missile went: 'HIT' or 'MISSED' shows for a while
launcher_news:
    cmp byte [has_launcher], 0
    je .done
    mov dx, LAUNCHER + 11
    in al, dx
    cmp al, [last_result]
    je .same
    mov [last_result], al
    cmp al, 2
    jb .same
    mov word [result_wait], RESULT_SHOWN
.same:
    cmp word [result_wait], 0
    je .shown
    dec word [result_wait]
.shown:
    cmp word [launch_wait], 0
    je .done
    dec word [launch_wait]
.done:
    ret

; The lead point to the laser's sight, while the laser is selected
laser_sight:
    cmp byte [weapon], WEAPON_LASER
    jne .done
    mov ax, [aim]
    mov dx, LASER + 8
    out dx, ax
    mov ax, [aim + 2]
    mov dx, LASER + 10
    out dx, ax
    mov ax, [aim + 4]
    mov dx, LASER + 12
    out dx, ax
    mov al, 1
    mov dx, LASER + 14
    out dx, al
.done:
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

; lead = AX metres * 1024 / muzzle (m/s; 1/1024 s). Changes BX, CX, DX.
lead_time:
    mov cx, 1024
    mul cx
    mov bx, ax
    mov cx, [muzzle]
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

; ---------- the screens ----------

draw:
    cmp word [r_seg], 0
    je .no_scope
    call draw_scope
.no_scope:
    cmp word [w_seg], 0
    je .no_page
    call draw_page
.no_page:
    ret

; The scope on the RADAR screen, a B-scope: bearing across, range up. The
; grid stays drawn; the marks are XORed onto it, so drawing a mark again
; rubs it out and leaves the grid as it was.
draw_scope:
    mov es, [r_seg]
    call put_marks                  ; the old marks off
    mov byte [mark_count], 0

    mov dx, RADAR + 3               ; the contacts now
    in al, dx
    xor ah, ah
    mov cx, ax
    cmp cx, MAX_MARKS
    jbe .fits
    mov cx, MAX_MARKS
.fits:
    mov dx, RADAR + 8
    in ax, dx
    mov [locked_id], ax
    mov si, [table_off]
    mov di, marks
.contact:
    jcxz .placed
    dec cx
    push cx
    push ds
    mov ds, [cs:radar_seg]
    mov ax, [si + 4]                ; bearing
    mov bx, [si + 2]                ; range, m
    mov bp, [si]                    ; id
    mov dl, [si + 10]               ; flags
    pop ds
    mov [mark_flags], dl
    add si, 16
    call place_mark
    pop cx
    jmp .contact
.placed:
    jmp put_marks                   ; and the new ones on

; AX bearing, BX range, BP id: a mark at DI if it is on the scope
place_mark:
    add ax, SCOPE_HALF
    jl .off
    cmp ax, 2 * SCOPE_HALF
    jg .off
    cmp bx, [scope_range]
    jae .off
    mov cx, [r_w]                   ; x = bearing across the width
    dec cx
    mul cx
    mov cx, 2 * SCOPE_HALF
    div cx
    mov [di], ax
    mov ax, bx                      ; y = range up the height
    mov cx, [r_h]
    dec cx
    mul cx
    div word [scope_range]
    mov dx, [r_h]
    dec dx
    sub dx, ax
    mov [di + 2], dx
    xor al, al
    cmp bp, [locked_id]
    jne .kind
    inc al
.kind:
    test byte [mark_flags], 2       ; a strobe: a line, not a blob
    jz .store
    mov al, 2
.store:
    mov [di + 4], al
    add di, 6
    inc byte [mark_count]
.off:
    ret

; XOR every mark in the list onto the scope
put_marks:
    mov si, marks
    mov cl, [mark_count]
    xor ch, ch
.next:
    jcxz .done
    dec cx
    push cx
    push si
    mov cx, [si]
    mov dx, [si + 2]
    mov bl, [si + 4]
    call xor_mark
    pop si
    pop cx
    add si, 6
    jmp .next
.done:
    ret

; A mark at (CX, DX): a bright 5 x 4 blob; BL 1 adds the lock's brackets
xor_mark:
    cmp bl, 2
    jne .blob
    jmp xor_strobe
.blob:
    cmp cx, 7                       ; kept inside the frame
    jge .x_low
    mov cx, 7
.x_low:
    mov ax, [r_w]
    sub ax, 8
    cmp cx, ax
    jle .x_high
    mov cx, ax
.x_high:
    cmp dx, 5
    jge .y_low
    mov dx, 5
.y_low:
    mov ax, [r_h]
    sub ax, 6
    cmp dx, ax
    jle .y_high
    mov dx, ax
.y_high:
    push cx
    push dx
    sub cx, 2
    sub dx, 2
    mov ax, 0FFC0h                  ; five bright pixels
    mov bp, 4
    call xor_rows
    pop dx
    pop cx
    test bl, bl
    jz .done
    sub dx, 4                       ; the brackets: half bright, 9 high
    push cx
    push dx
    sub cx, 5
    mov ax, 8000h
    mov bp, 9
    call xor_rows
    pop dx
    pop cx
    add cx, 5
    mov ax, 8000h
    mov bp, 9
    call xor_rows
.done:
    ret

; A jammer's strobe at x = CX: a dim line up the scope
xor_strobe:
    mov dx, 5
    mov bp, [r_h]
    sub bp, 10
    mov ax, 4000h                   ; one dim pixel a row
    jmp xor_rows

; XOR the pixels AX (the leftmost in the top bits) onto BP rows from
; (CX, DX) down
; A zoom key for the scope: AL the key. The marks follow at the next drawing.
scope_key:
    cmp word [r_seg], 0
    je .done
    mov bl, [r_scale]
    cmp al, KEY_IN
    jne .out
    cmp bl, SCOPE_SCALES - 1
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
    push es
    mov es, [r_seg]
    push bx
    call scope_label                ; the old range off
    pop bx
    mov [r_scale], bl
    call scope_label                ; the new one on
    pop es
.done:
    ret

; The scale in use: scope_range, and its label XORed in the top left
; corner (ES the scope)
scope_label:
    mov bl, [r_scale]
    xor bh, bh
    shl bx, 1
    mov ax, [scope_scales + bx]
    mov [scope_range], ax
    mov al, [scope_digits + bx]     ; its digit (a word apart, the same index)
    mov cx, 3
    mov dx, 3
    call xor_glyph
    mov al, 10                      ; K
    mov cx, 9
    mov dx, 3
    jmp xor_glyph

; AL a glyph (0 to 9 the digits, 10 K): XORed 5 x 5 with its top left at
; (CX, DX) on the scope
xor_glyph:
    mov bl, 10
    mul bl
    mov si, ax
    add si, glyphs
    mov ax, dx
    mul word [r_pitch]
    mov di, cx
    shr di, 1
    shr di, 1
    add di, ax
    and cl, 3
    shl cl, 1
    mov bp, 5
.row:
    mov ax, [si]
    shr ax, cl
    xor [es:di], ah
    xor [es:di + 1], al
    add si, 2
    add di, [r_pitch]
    dec bp
    jnz .row
    ret

xor_rows:
    push ax
    mov ax, dx
    mul word [r_pitch]
    mov di, cx
    shr di, 1
    shr di, 1
    add di, ax
    and cl, 3
    shl cl, 1
    pop ax
    shr ax, cl
.row:
    xor [es:di], ah
    xor [es:di + 1], al
    add di, [r_pitch]
    dec bp
    jnz .row
    ret

; The page on the WEAPON screen
draw_page:
    mov es, [w_seg]
    cmp byte [weapon], 0
    je .gun
    cmp byte [weapon], WEAPON_LASER
    je .laser
    jmp draw_missile_page
.laser:
    mov dx, LASER + 2               ; its status: bit 2 too hot, as the turret's
    in al, dx
    and al, 4
    mov [turret_status], al
    mov word [heat_port], LASER + 3
    jmp .status
.gun:
    mov dx, TURRET + 13
    in al, dx
    mov [turret_status], al
    mov word [heat_port], TURRET + 14
.status:

    mov di, (VALUE_COL << 8) | 2    ; the target
    call at
    xor ax, ax
    cmp byte [locked], 0
    je .no_target
    mov ax, [track]
.no_target:
    call put_number
    mov di, (VALUE_COL << 8) | 3
    call at
    xor ax, ax
    cmp byte [locked], 0
    je .no_range
    mov ax, [range]
.no_range:
    call put_unsigned
    mov di, (VALUE_COL << 8) | 4    ; lead time, ms (1/1024 s, near enough)
    call at
    xor ax, ax
    cmp byte [locked], 0
    je .no_lead
    mov ax, [lead]
.no_lead:
    call put_unsigned
    mov di, (VALUE_COL << 8) | 5    ; how clearly the radar sees it
    call at
    xor ax, ax
    cmp byte [locked], 0
    je .no_snr
    mov ax, [snr]
.no_snr:
    call put_unsigned

    mov ax, [turret_az]             ; the turret, degrees; for the laser,
    mov bx, [turret_el]             ; the lead point off the nose
    cmp byte [weapon], WEAPON_LASER
    jne .angles
    mov ax, [aim_az]
    mov bx, [aim_el]
.angles:
    push bx
    mov di, (VALUE_COL << 8) | 6
    call at
    mov cx, 360
    imul cx
    mov ax, dx
    call put_number
    mov di, (VALUE_COL << 8) | 7
    call at
    pop ax
    mov cx, 360
    imul cx
    mov ax, dx
    call put_number

    call put_heat
    call put_status

put_load:
    mov di, (VALUE_COL << 8) | 9    ; CPU load
    call at
    mov ax, [busy]
    mov dx, [busy + 2]
    mov cx, 1193
    cmp dx, cx
    jae .full
    div cx
    jmp .load
.full:
    mov ax, 100
.load:
    jmp put_unsigned

; The missile page: the target, the missiles, the newest one's flight
draw_missile_page:
    mov di, (VALUE_COL << 8) | 2
    call at
    mov ax, [track]
    call if_locked
    call put_number
    mov di, (VALUE_COL << 8) | 3
    call at
    mov ax, [range]
    call if_locked
    call put_unsigned
    mov di, (VALUE_COL << 8) | 4
    call at
    mov ax, [snr]
    call if_locked
    call put_unsigned
    mov di, (VALUE_COL << 8) | 5    ; missiles left
    call at
    call missiles_left
    xor ah, ah
    call put_unsigned
    mov di, (VALUE_COL << 8) | 6    ; in flight
    call at
    mov dx, LAUNCHER + 3
    in al, dx
    xor ah, ah
    call put_unsigned
    mov di, (VALUE_COL << 8) | 7    ; time to go, seconds
    call at
    mov dx, LAUNCHER + 8
    in ax, dx
    cmp ax, 0FFFFh
    jne .tgo
    xor ax, ax
.tgo:
    mov cl, 3
    shr ax, cl
    call put_unsigned
    mov di, (VALUE_COL << 8) | 8    ; its fuel
    call at
    mov dx, LAUNCHER + 12
    in ax, dx
    call put_unsigned
    call put_missile_status
    jmp put_load

; AL = how many of the weapon selected are left
missiles_left:
    mov dx, LAUNCHER + 2
    cmp byte [weapon], 2
    jne .radar
    mov dx, LAUNCHER + 14
.radar:
    in al, dx
    ret

; AX, or 0 with nothing locked
if_locked:
    cmp byte [locked], 0
    jne .yes
    xor ax, ax
.yes:
    ret

put_missile_status:
    mov si, s_launch
    mov ah, ATTR_WARN
    cmp word [launch_wait], 0
    je .no_launch
    jmp .put
.no_launch:
    mov dx, LAUNCHER + 11
    in al, dx
    cmp al, 1
    jne .not_flying
    mov dx, LAUNCHER + 10
    in al, dx
    mov si, s_lost
    test al, 4
    jnz .put
    test al, 2
    jz .heard
    mov si, s_no_link
    mov dx, LAUNCHER + 15
    in al, dx
    cmp al, 2
    jne .put
    mov si, s_no_heat               ; a heat seeker's head sees nothing
    jmp .put
.heard:
    mov si, s_guiding
    mov ah, ATTR_LOCKED
    test al, 1
    jnz .put
    mov si, s_search                ; a heat seeker that has seen nothing yet
    mov ah, ATTR_LABEL
    jmp .put
.not_flying:
    cmp word [result_wait], 0
    je .idle
    mov si, s_missed
    cmp al, 2
    jne .put
    mov si, s_hit
    mov ah, ATTR_LOCKED
    jmp .put
.idle:
    call missiles_left
    mov si, s_empty
    test al, al
    jz .put
    cmp byte [weapon], 2
    je .head
    mov si, s_no_target
    mov ah, ATTR_LABEL
    cmp byte [locked], 0
    je .put
    mov si, s_msl_ready
    mov ah, ATTR_LOCKED
    jmp .put
.head:
    call head_status
.put:
    mov di, 1
    call at
    jmp put_text

; SI, AH = what the heat seeker's head is doing: off, searching, a tone, locked
head_status:
    mov si, s_head_off
    mov ah, ATTR_LABEL
    cmp byte [head_on], 0
    je .done
    mov dx, LAUNCHER + 4
    in al, dx
    mov si, s_ir_lock
    mov ah, ATTR_LOCKED
    test al, 16
    jnz .done
    mov si, s_ir_tone
    test al, 8
    jnz .done
    mov si, s_search
    mov ah, ATTR_LABEL
.done:
    ret

; The barrel's heat as a bar, green, yellow from half way, red too hot
put_heat:
    mov dx, [heat_port]
    in al, dx
    mov bh, ATTR_CONTACT
    cmp al, 128
    jb .colour
    mov bh, ATTR_LOCKED
.colour:
    test byte [turret_status], 4
    jz .cool
    mov bh, ATTR_WARN
.cool:
    mov cl, HEAT_CELLS
    mul cl
    add ax, 255
    mov bl, ah                      ; cells lit: heat * cells / 256, rounded up
    mov di, (HEAT_COL << 8) | 8
    call at
    mov cx, HEAT_CELLS
.cell:
    mov ax, (ATTR_SCOPE << 8) | 0FAh
    test bl, bl
    jz .put
    dec bl
    mov al, 0DBh
    mov ah, bh
.put:
    stosw
    loop .cell
    ret

; What it is doing
put_status:
    mov si, s_no_target
    mov ah, ATTR_LABEL
    mov al, [turret_status]
    test al, 4
    jz .not_hot
    mov si, s_too_hot
    cmp byte [weapon], WEAPON_LASER
    jne .hot
    mov si, s_laser_hot
.hot:
    mov ah, ATTR_WARN
    jmp .put
.not_hot:
    test al, 2                      ; the target is where the turret cannot go
    jz .in_arc
    cmp byte [locked], 0
    je .in_arc
    mov si, s_out_of_arc
    mov ah, ATTR_WARN
    jmp .put
.in_arc:
    cmp byte [firing], 0
    je .not_firing
    mov si, s_firing
    mov ah, ATTR_WARN
    jmp .put
.not_firing:
    cmp byte [locked], 0
    je .put
    mov si, s_tracking
    mov ah, ATTR_LOCKED
.put:
    mov di, 1                       ; column 0, row 1
    call at
    jmp put_text

; DI = column << 8 | row  ->  DI = its cell's offset on the WEAPON screen
at:
    push ax
    push dx
    mov ax, di
    mov dl, [w_cols]
    mul dl
    mov dx, di
    mov dl, dh
    xor dh, dh
    add ax, dx
    shl ax, 1
    mov di, ax
    pop dx
    pop ax
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

; The page's labels, for the weapon selected
draw_labels:
    push es
    mov es, [w_seg]
    xor di, di
    mov ax, (ATTR_LABEL << 8) | ' '
    mov cx, [w_cells]
    rep stosw
    mov si, labels
    cmp byte [weapon], 0
    je .next
    mov si, laser_labels
    cmp byte [weapon], WEAPON_LASER
    je .next
    mov si, m_labels
    cmp byte [weapon], 2
    jne .next
    mov si, ir_labels
.next:
    lodsw
    cmp ax, 0FFFFh
    je .done
    cmp ax, 0FFFEh
    jne .gun_rest
    mov si, m_body
    jmp .next
.gun_rest:
    cmp ax, 0FFFDh
    jne .label
    mov si, gun_body
    jmp .next
.label:
    mov di, ax
    call at
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

; column << 8 | row, attribute, text
laser_labels:
    dw 0
    db ATTR_TITLE, ' FIRECTL  LSR ', 0
    dw 0FFFDh                       ; the rest as the gun page
labels:
    dw 0
    db ATTR_TITLE, ' FIRECTL  GUN ', 0
gun_body:
    dw 2
    db ATTR_LABEL, 'TGT', 0
    dw 3
    db ATTR_LABEL, 'RNG', 0
    dw (10 << 8) | 3
    db ATTR_LABEL, 'm', 0
    dw 4
    db ATTR_LABEL, 'LEAD', 0
    dw (10 << 8) | 4
    db ATTR_LABEL, 'ms', 0
    dw 5
    db ATTR_LABEL, 'SNR', 0
    dw (10 << 8) | 5
    db ATTR_LABEL, 'dB', 0
    dw 6
    db ATTR_LABEL, 'AZ', 0
    dw (10 << 8) | 6
    db ATTR_LABEL, 'deg', 0
    dw 7
    db ATTR_LABEL, 'EL', 0
    dw (10 << 8) | 7
    db ATTR_LABEL, 'deg', 0
    dw 8
    db ATTR_LABEL, 'HT', 0
    dw 9
    db ATTR_LABEL, 'CPU', 0
    dw (10 << 8) | 9
    db ATTR_LABEL, '%', 0
    dw 0FFFFh

ir_labels:
    dw 0
    db ATTR_TITLE, ' FIRECTL  IR  ', 0
    dw 0FFFEh                       ; the rest as the missile page
m_labels:
    dw 0
    db ATTR_TITLE, ' FIRECTL  MSL ', 0
m_body:
    dw 2
    db ATTR_LABEL, 'TGT', 0
    dw 3
    db ATTR_LABEL, 'RNG', 0
    dw (10 << 8) | 3
    db ATTR_LABEL, 'm', 0
    dw 4
    db ATTR_LABEL, 'SNR', 0
    dw (10 << 8) | 4
    db ATTR_LABEL, 'dB', 0
    dw 5
    db ATTR_LABEL, 'LFT', 0
    dw 6
    db ATTR_LABEL, 'FLY', 0
    dw 7
    db ATTR_LABEL, 'TGO', 0
    dw (10 << 8) | 7
    db ATTR_LABEL, 's', 0
    dw 8
    db ATTR_LABEL, 'DV', 0
    dw (10 << 8) | 8
    db ATTR_LABEL, 'm/s', 0
    dw 9
    db ATTR_LABEL, 'CPU', 0
    dw (10 << 8) | 9
    db ATTR_LABEL, '%', 0
    dw 0FFFFh

s_no_target     db 'NO TARGET  ', 0
s_tracking      db 'TRACKING   ', 0
s_firing        db 'FIRING     ', 0
s_too_hot       db 'GUN TOO HOT', 0
s_laser_hot     db 'LSR TOO HOT', 0
s_out_of_arc    db 'OUT OF ARC ', 0
s_msl_ready     db 'MSL READY  ', 0
s_launch        db 'LAUNCH     ', 0
s_guiding       db 'GUIDING    ', 0
s_no_link       db 'NO LINK    ', 0
s_lost          db 'MSL LOST   ', 0
s_hit           db 'HIT        ', 0
s_missed        db 'MISSED     ', 0
s_empty         db 'NO MISSILES', 0
s_ir_tone       db 'IR TONE    ', 0
s_no_heat       db 'NO HEAT    ', 0
s_ir_lock       db 'IR LOCK    ', 0
s_search        db 'SEARCH     ', 0
s_head_off      db 'SEEKER OFF ', 0

buttons         db 0
pressed         db 0
locked          db 0
firing          db 0
rounds_left     db 0
screen_wait     db 1
turret_status   db 0
weapon          db 0                ; 0 the cannon, 1 radar missiles, 2 heat seekers, 3 the laser
has_launcher    db 0
has_laser       db 0
has_turret      db 0
head_on         db 0                ; the heat seeker's head powered
last_result     db 0
launch_wait     dw 0
result_wait     dw 0
mark_count      db 0
mark_flags      db 0                ; the contact's flags, while placing its mark
r_scale         db 0                ; which of scope_scales
r_screen        db 0FFh             ; the RADAR screen's place in the screen table
keys_seen       db 0                ; the display card's key count, last look
scope_range     dw 8000             ; metres to the top of the scope
scope_scales    dw 8000, 6000, 4000, 2000, 1000
scope_digits    dw 8, 6, 4, 2, 1
; 0 to 9 and K, 5 x 5 in bright 2-bit pixels, a word a row
glyphs:
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
                dw 0C0C0h, 0C300h, 0FC00h, 0C300h, 0C0C0h    ; K
r_seg           dw 0                ; the RADAR screen's segment (0: none)
r_w             dw 0                ; its size in pixels
r_h             dw 0
r_pitch         dw 0                ; bytes a row
r_words         dw 0                ; words in all
w_seg           dw 0                ; the WEAPON screen's segment (0: none)
w_cells         dw 0
w_cols          db 0
marks           times MAX_MARKS * 3 dw 0    ; x, y, 1 if locked
radar_seg       dw 0
table_off       dw 0
track_off       dw 0
locked_id       dw 0
turret_az       dw 0
turret_el       dw 0
aim_az          dw 0
aim_el          dw 0
lead            dw 0
muzzle          dw 0                ; m/s: the speed of what the solution is for
heat_port       dw TURRET + 14      ; the page's heat bar
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
    mov dx, DISPLAYS
    mov bl, TYPE_DISPLAYS
    call check_device
    mov dx, LAUNCHER                ; a launcher, if the ship has one
    in al, dx
    cmp al, TYPE_LAUNCHER
    jne .no_launcher
    mov byte [has_launcher], 1
.no_launcher:
    mov dx, LASER                   ; and a pulse laser
    in al, dx
    cmp al, TYPE_LASER
    jne .no_laser
    mov byte [has_laser], 1
.no_laser:
    mov dx, TURRET                  ; the turret, or else the laser
    in al, dx
    cmp al, TYPE_TURRET
    jne .no_turret
    mov byte [has_turret], 1
    jmp .armed
.no_turret:
    mov dx, LASER
    mov bl, TYPE_LASER
    call check_device
    mov byte [weapon], WEAPON_LASER
.armed:

    mov dx, RADAR + 10              ; the radar's picture
    in ax, dx
    mov [radar_seg], ax
    mov dx, RADAR + 12
    in ax, dx
    mov [table_off], ax
    mov dx, RADAR + 14
    in ax, dx
    mov [track_off], ax
    mov si, n_radar                 ; the screens it uses, if the ship has them
    call find_screen
    jc .no_scope
    cmp word [es:bx + 10], 32
    jb .no_scope
    cmp word [es:bx + 12], 16
    jb .no_scope
    mov ax, bx                      ; its place in the screen table
    push ax
    mov dx, DISPLAYS + 4
    in ax, dx
    mov cx, ax
    pop ax
    sub ax, cx
    mov cl, 4
    shr ax, cl
    mov [r_screen], al
    mov dx, DISPLAYS + 8
    in al, dx
    mov [keys_seen], al
    mov byte [es:bx + 6], 1         ; graphics
    mov ax, [es:bx + 10]
    mov [r_w], ax
    shr ax, 1
    shr ax, 1
    mov [r_pitch], ax
    mov cx, [es:bx + 12]
    mov [r_h], cx
    mul cx
    inc ax
    shr ax, 1
    mov [r_words], ax
    call screen_segment
    mov [r_seg], ax
    call draw_grid
    push es
    mov es, [r_seg]
    call scope_label
    pop es
.no_scope:
    mov si, n_weapon
    call find_screen
    jc .no_page
    cmp byte [es:bx + 7], 14
    jb .no_page
    cmp byte [es:bx + 8], 10
    jb .no_page
    mov byte [es:bx + 6], 0         ; text
    mov al, [es:bx + 7]
    mov [w_cols], al
    mul byte [es:bx + 8]
    mov [w_cells], ax
    call screen_segment
    mov [w_seg], ax
    call draw_labels
.no_page:

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
    call unlink                     ; out of the IRQ 5 chain, wherever it is
    xor al, al                      ; radar off
    mov dx, RADAR + 2
    out dx, al
    mov dx, LAUNCHER + 7            ; the heat seeker's head off
    out dx, al
    mov ax, [es:w_seg]              ; the resident copy's screens
    mov [w_seg], ax
    mov al, [es:w_cols]
    mov [w_cols], al
    mov ax, [es:r_seg]
    mov [r_seg], ax
    mov ax, [es:r_words]
    mov [r_words], ax
    push es
    cmp word [w_seg], 0
    je .no_page
    mov es, [w_seg]
    mov si, s_removed
    mov di, 1
    call at
    mov ah, ATTR_WARN
    call put_text
.no_page:
    cmp word [r_seg], 0             ; the scope goes dark
    je .no_scope
    mov es, [r_seg]
    xor di, di
    xor ax, ax
    mov cx, [r_words]
    rep stosw
.no_scope:
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

; The scope's grid: the frame, a dotted line every 2 km and dead ahead,
; ticks at 30 degrees either side
draw_grid:
    push es
    mov es, [r_seg]
    xor di, di
    mov cx, [r_words]
    xor ax, ax
    rep stosw
    mov bl, 2                       ; the frame
    mov bp, 1
    xor dx, dx
    call hline
    mov dx, [r_h]
    dec dx
    call hline
    xor cx, cx
    call vline
    mov cx, [r_w]
    dec cx
    call vline
    mov bl, 1                       ; the ranges, dotted
    mov bp, 4
    mov si, 1
.range:
    mov ax, [r_h]
    dec ax
    mul si
    shr ax, 1
    shr ax, 1
    mov dx, [r_h]
    dec dx
    sub dx, ax
    call hline
    inc si
    cmp si, 4
    jb .range
    mov cx, [r_w]                   ; dead ahead, dotted
    dec cx
    shr cx, 1
    call vline
    mov bl, 2                       ; 30 degrees either side
    mov cx, [r_w]
    dec cx
    shr cx, 1
    shr cx, 1
    call ticks
    mov ax, [r_w]
    dec ax
    mov cx, ax
    shr ax, 1
    shr ax, 1
    sub cx, ax
    call ticks
    pop es
    ret

; Row DX, every BP-th pixel, value BL
hline:
    xor cx, cx
.next:
    call plot
    add cx, bp
    cmp cx, [r_w]
    jb .next
    ret

; Column CX, every BP-th pixel, value BL
vline:
    xor dx, dx
.next:
    call plot
    add dx, bp
    cmp dx, [r_h]
    jb .next
    ret

; Column CX: 4 pixels down from the top and up from the bottom
ticks:
    mov si, 4
    mov dx, 1
.top:
    call plot
    inc dx
    dec si
    jnz .top
    mov si, 4
    mov dx, [r_h]
    sub dx, 2
.bottom:
    call plot
    dec dx
    dec si
    jnz .bottom
    ret

; OR value BL into the pixel at (CX, DX)
plot:
    push ax
    push cx
    push dx
    push di
    mov ax, dx
    mul word [r_pitch]
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
    ret

n_radar         db 'RADAR '
n_weapon        db 'WEAPON'

s_removed       db 'REMOVED    ', 0
msg_installed   db 'Fire control resident: scope on RADAR, page on WEAPON.', 13, 10
                db 'FIRECTL OFF removes it.', 13, 10, '$'
msg_already     db 'Fire control is already resident.', 13, 10, '$'
msg_not_running db 'Fire control is not resident, or a program that is not', 13, 10
                db "one of the ship's took IRQ 5 after it.", 13, 10, '$'
msg_removed     db 'Fire control removed: radar off.', 13, 10, '$'
msg_no_device   db 'FIRECTL: the radar, the turret or the side screens', 13, 10
                db 'are not on the bus.', 13, 10, '$'
