; firectl.asm - CucsiCorp Fire Control 1.15 (FIRECTL.COM)
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
; zoom keys step the range; while another program has the screen lent
; (its entry's +9 not 0, as FLIGHT takes it to show a landing pad) the
; scope is left alone, and drawn afresh when the screen comes back) and
; a page on WEAPON (graphics, 112 x 160 or more, to be read at a glance: the
; weapon's name, its rounds or its heat, the distance to the target, a word
; on what is going on). Without them it works the same, unseen.
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
;                            +7 columns  +8 rows  +9 lent (version 4: 0, or
;                            the mark of a program that has taken it)
;                            +10 width  +12 height  +14 memory (offset)
;   160h  missile launcher   +0 type 7  +2 radar missiles left  +3 in flight
;                            +4 status (bit 0 radar missile ready, 2 heat
;                            seeker ready, 3 its head sees heat, 4 locked on)
;                            +6 fire (write 1 radar, 2 heat seeker)
;                            +7 bit 0 the heat seeker's head powered
;                            the newest missile: +8 time to go (1/8 s)
;                            +10 status (bit 1 no word, 2 lost)  +11 1 flying,
;                            2 hit, 3 missed  +12 fuel (m/s)  +14 heat
;                            seekers left  +15 the newest's kind (2 heat)
;   230h  interdictor        +0 type 21  +2 status: bit 0 on, 1 on a target
;                            +5 write: 1 on, 0 off  +7 its button (it thickens
;                            the medium round the ship the radar tracks)
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
INTERDICTOR equ 230h
TYPE_RADAR  equ 4
TYPE_TURRET equ 5
TYPE_DISPLAYS equ 6
TYPE_LAUNCHER equ 7
TYPE_LASER  equ 11
TYPE_INTERDICTOR equ 21
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
MAX_MARKS   equ 16                  ; contacts drawn on the scope
PAGE_MIN_W  equ 112                 ; the page, in the WEAPON screen's pixels
PAGE_MIN_H  equ 160
PAGE_LEFT   equ 2
PAGE_WIDTH  equ 108
NAME_Y      equ 1
RULE_1      equ 60
SLOT_LABEL_Y equ 62
SLOT_Y      equ 76
SLOT_HEIGHT equ 24
RULE_2      equ 101
DIST_LABEL_Y equ 103
DIST_Y      equ 117
RULE_3      equ 142
STATUS_Y    equ 145
SMALL_HEIGHT equ 13
BAR_LEFT    equ 4                   ; the heat bar inside the slot
BAR_WIDTH   equ 104
MID_CELL    equ 14                  ; a digit's width
VALUE_LEFT  equ 32                  ; five of them and their gaps, to the right
INK_DIM     equ 55h                 ; the screen's three colours, four pixels of each
INK_BRIGHT  equ 0AAh
INK_RED     equ 0FFh

    jmp start

; ======================================================================
; The resident part
; ======================================================================

signature   db 'XPFC15'             ; 103h
old_irq     dw 0, 0                 ; 109h
weapon      db 0                    ; 10Dh  0 the cannon, 1 radar missiles, 2 heat seekers, 3 the laser
cpu_load    db 0                    ; 10Eh  % of the processor it took, last time it drew
page_status dw 0                    ; 10Fh  the word on the page (0: none)

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
    cmp byte [has_interdictor], 0   ; the interdictor, on the press of its button
    je .no_interdictor
    mov dx, INTERDICTOR + 7
    in al, dx
    mov ah, [interdict_button]
    mov [interdict_button], al
    not ah
    and al, ah
    jz .no_interdictor
    mov dx, INTERDICTOR + 2
    in al, dx
    not al
    and al, 1
    mov dx, INTERDICTOR + 5
    out dx, al
.no_interdictor:
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
    call draw_frame                 ; the other weapon's page
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
    call scope_lent
    jc .no_scope
    call draw_scope
.no_scope:
    cmp word [w_seg], 0
    je .no_page
    call draw_page
.no_page:
    ret

; The RADAR screen lent to another program (its entry's +9 marked): CF
; set, and the scope is left alone. Given back, it is drawn afresh.
scope_lent:
    push es
    les bx, [r_entry]
    cmp byte [es:bx + 9], 0
    je .ours
    mov byte [r_lent], 1
    pop es
    stc
    ret
.ours:
    cmp byte [r_lent], 0
    je .done
    mov byte [r_lent], 0
    mov byte [es:bx + 6], 1         ; graphics again
    mov byte [mark_count], 0        ; the marks went with the picture
    call draw_grid
    mov es, [r_seg]
    call scope_label
.done:
    pop es
    clc
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
    cmp byte [r_lent], 0            ; not while another program has it
    jne .done
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

; The page on the WEAPON screen: the weapon's name, large; under it its
; rounds, or the heat of what has no rounds to count; the distance to the
; target; a word on what is going on. Only what has changed is drawn.
draw_page:
    mov es, [w_seg]
    mov ax, [busy]                  ; the processor's load, for whoever looks
    mov dx, [busy + 2]
    mov cx, 1193
    cmp dx, cx
    jae .full
    div cx
    jmp .load
.full:
    mov al, 100
.load:
    mov [cpu_load], al
    call put_slot
    call put_distance
    jmp put_status

; The page anew, for the weapon selected
draw_frame:
    push es
    mov es, [w_seg]
    xor di, di
    xor ax, ax
    mov cx, [w_words]
    rep stosw

    mov al, [weapon]                ; its name
    xor ah, ah
    shl ax, 1
    shl ax, 1
    mov bx, ax
    mov si, [bx + names]
    mov cx, [bx + names + 2]
    mov dx, NAME_Y
    mov bx, big_font
    mov al, INK_BRIGHT
    call draw_string

    mov al, INK_DIM                 ; the rules between the parts
    mov cx, PAGE_LEFT
    mov si, PAGE_WIDTH
    mov bp, 1
    mov dx, RULE_1
    call fill_rect
    mov dx, RULE_2
    call fill_rect
    mov dx, RULE_3
    call fill_rect

    mov si, s_ammo                  ; what the slot under the name shows
    cmp byte [weapon], 0
    je .bar
    cmp byte [weapon], WEAPON_LASER
    jne .slot
.bar:
    mov al, INK_DIM                 ; the heat bar's frame
    mov cx, PAGE_LEFT
    mov si, PAGE_WIDTH
    mov bp, 1
    mov dx, SLOT_Y
    call fill_rect
    mov dx, SLOT_Y + SLOT_HEIGHT - 1
    call fill_rect
    mov si, 1
    mov bp, SLOT_HEIGHT
    mov dx, SLOT_Y
    call fill_rect
    mov cx, PAGE_LEFT + PAGE_WIDTH - 1
    call fill_rect
    mov si, s_heat
.slot:
    mov cx, PAGE_LEFT
    mov dx, SLOT_LABEL_Y
    mov bx, small_font
    mov al, INK_DIM
    call draw_string
    mov si, s_dist
    mov cx, PAGE_LEFT
    mov dx, DIST_LABEL_Y
    mov al, INK_DIM
    call draw_string

    mov word [shown_slot], 0FFFFh   ; nothing of what comes is on the page yet
    mov word [page_status], 0
    mov ax, 2020h
    mov [shown_ammo], ax
    mov [shown_ammo + 2], ax
    mov [shown_ammo + 4], al
    mov [shown_dist], ax
    mov [shown_dist + 2], ax
    mov [shown_dist + 4], al
    pop es
    ret

; The slot under the name: a missile's rounds, or the heat of the rest
put_slot:
    cmp byte [weapon], 0
    je .gun
    cmp byte [weapon], WEAPON_LASER
    je .laser

    call missiles_left
    xor ah, ah
    cmp ax, [shown_slot]
    je .done
    cmp word [shown_slot], 0        ; 'NO AMMO' is wider than the numbers' place
    mov [shown_slot], ax
    jne .not_from_none
    push ax
    call clear_slot
    pop ax
.not_from_none:
    test ax, ax
    jz .none
    mov bx, shown_ammo
    mov dx, SLOT_Y
    jmp put_value
.none:
    call clear_slot
    mov si, s_no_ammo
    mov cx, PAGE_LEFT - 1
    mov dx, SLOT_Y
    mov bx, mid_font
    mov al, INK_RED
    call draw_string
    mov al, 0FFh                    ; the numbers' place has to be drawn afresh
    mov di, shown_ammo
    push es
    push ds
    pop es
    mov cx, 5
    rep stosb
    pop es
.done:
    ret

.laser:
    mov dx, LASER + 2               ; its status: bit 2 too hot, as the turret's
    in al, dx
    mov [turret_status], al
    mov dx, LASER + 3
    jmp .heat
.gun:
    mov dx, TURRET + 13
    in al, dx
    mov [turret_status], al
    mov dx, TURRET + 14
.heat:
    in al, dx
    mov cl, BAR_WIDTH
    mul cl                          ; AH = the pixels lit: heat * width / 256
    mov al, [turret_status]
    and al, 4                       ; and too hot or not
    cmp ax, [shown_slot]
    je .done
    mov [shown_slot], ax
    mov bl, ah
    xor bh, bh
    mov ah, INK_BRIGHT
    test al, al
    jz .ink
    mov ah, INK_RED
.ink:
    mov al, ah
    mov si, bx
    mov cx, BAR_LEFT
    mov dx, SLOT_Y + 2
    mov bp, SLOT_HEIGHT - 4
    call fill_rect
    add cx, si
    mov ax, BAR_WIDTH
    sub ax, si
    mov si, ax
    xor al, al
    jmp fill_rect

clear_slot:
    mov cx, PAGE_LEFT - 1
    mov dx, SLOT_Y
    mov si, PAGE_WIDTH + 2
    mov bp, SLOT_HEIGHT
    xor al, al
    jmp fill_rect

; The distance to the target, to 10 m: what the eye can use, and the last
; digit needs no drawing
put_distance:
    mov ax, 0FFFFh
    cmp byte [locked], 0
    je .show
    mov ax, [range]
    cmp ax, 65530
    jbe .round
    mov ax, 65530
.round:
    add ax, 5
    xor dx, dx
    mov cx, 10
    div cx
    mul cx
.show:
    mov bx, shown_dist
    mov dx, DIST_Y
    jmp put_value

; AX on the row DX, on the right of the page, in the digits that have
; changed since the ones at BX; 0FFFFh is a row of dashes
put_value:
    push bx
    mov di, digits
    mov cx, 5
.blank:
    mov byte [di], ' '
    inc di
    loop .blank
    cmp ax, 0FFFFh
    jne .number
    mov byte [digits + 2], '-'
    mov byte [digits + 3], '-'
    mov byte [digits + 4], '-'
    jmp .shown
.number:
    mov di, digits + 4
    mov cx, 10
    mov bx, dx                      ; the row, while dividing
.digit:
    xor dx, dx
    div cx
    add dl, '0'
    mov [di], dl
    dec di
    test ax, ax
    jnz .digit
    mov dx, bx
.shown:
    pop bx
    mov si, digits
    mov cx, VALUE_LEFT
    mov di, 5
.cell:
    lodsb
    cmp al, [bx]
    je .same
    mov [bx], al
    push si
    push di
    push bx
    push cx
    push dx
    push ax
    mov si, MID_CELL
    mov bp, SLOT_HEIGHT
    xor al, al
    call fill_rect
    pop ax
    mov bx, mid_font
    mov [font], bx
    mov [pen_x], cx
    mov [pen_y], dx
    mov byte [pen_ink], INK_BRIGHT
    cmp al, '-'
    jne .ink
    mov byte [pen_ink], INK_DIM
.ink:
    cmp al, ' '
    je .drawn
    call draw_char
.drawn:
    pop dx
    pop cx
    pop bx
    pop di
    pop si
.same:
    add cx, MID_CELL + 2
    inc bx
    dec di
    jnz .cell
    ret

; AL = how many of the weapon selected are left
missiles_left:
    mov dx, LAUNCHER + 2
    cmp byte [weapon], 2
    jne .radar
    mov dx, LAUNCHER + 14
.radar:
    in al, dx
    ret

; The word at the bottom, when there is something to say
put_status:
    cmp byte [has_interdictor], 0
    je .weapon
    mov dx, INTERDICTOR + 2
    in al, dx
    test al, 1
    jz .weapon
    mov si, s_interdict
    mov ah, INK_BRIGHT
    jmp .say
.weapon:
    cmp byte [weapon], 0
    je .gun
    cmp byte [weapon], WEAPON_LASER
    je .gun
    call missile_status
    jmp .say
.gun:
    xor si, si
    mov ah, INK_BRIGHT
    mov al, [turret_status]
    test al, 4
    jz .not_hot
    mov si, s_too_hot
    mov ah, INK_RED
    jmp .say
.not_hot:
    test al, 2                      ; the target is where the turret cannot go
    jz .say
    cmp byte [locked], 0
    je .say
    mov si, s_out_of_arc
    mov ah, INK_RED
.say:
    cmp si, [page_status]
    je .done
    mov [page_status], si
    push ax
    push si
    mov cx, PAGE_LEFT
    mov dx, STATUS_Y
    mov si, PAGE_WIDTH
    mov bp, SMALL_HEIGHT
    xor al, al
    call fill_rect
    pop si
    pop ax
    test si, si
    jz .done
    mov al, ah
    mov cx, PAGE_LEFT
    mov dx, STATUS_Y
    mov bx, small_font
    jmp draw_string
.done:
    ret

; SI, AH = what the missile is doing, and the ink
missile_status:
    mov si, s_launch
    mov ah, INK_RED
    cmp word [launch_wait], 0
    je .no_launch
    ret
.no_launch:
    mov dx, LAUNCHER + 15           ; news only of the kind selected: a heat
    in al, dx                       ; seeker's pilot wants to see its head
    cmp al, 2
    je .news_heat
    cmp byte [weapon], 2
    jne .news
    jmp .idle
.news_heat:
    cmp byte [weapon], 2
    jne .idle
.news:
    mov ah, INK_RED
    mov dx, LAUNCHER + 11
    in al, dx
    cmp al, 1
    jne .not_flying
    mov dx, LAUNCHER + 10
    in al, dx
    mov si, s_lost
    test al, 4
    jnz .done
    test al, 2
    jz .heard
    mov si, s_no_link
    mov dx, LAUNCHER + 15
    in al, dx
    cmp al, 2
    jne .done
    mov si, s_no_heat               ; a heat seeker's head sees nothing
    ret
.heard:
    mov si, s_guiding
    mov ah, INK_BRIGHT
    test al, 1
    jnz .done
    mov si, s_search                ; a heat seeker that has seen nothing yet
    mov ah, INK_DIM
    ret
.not_flying:
    cmp word [result_wait], 0
    je .idle
    mov si, s_missed
    cmp al, 2
    jne .done
    mov si, s_hit
    mov ah, INK_BRIGHT
    ret
.idle:
    call missiles_left
    xor si, si                      ; none left: the slot says it
    test al, al
    jz .done
    cmp byte [weapon], 2
    je .head
    mov si, s_no_target
    mov ah, INK_DIM
    cmp byte [locked], 0
    je .done
    mov si, s_msl_ready
    mov ah, INK_BRIGHT
    ret
.head:
    jmp head_status
.done:
    ret

; SI, AH = what the heat seeker's head is doing: off, searching, a tone, locked
head_status:
    mov si, s_head_off
    mov ah, INK_DIM
    cmp byte [head_on], 0
    je .done
    mov dx, LAUNCHER + 4
    in al, dx
    mov si, s_ir_lock
    mov ah, INK_BRIGHT
    test al, 16
    jnz .done
    mov si, s_ir_tone
    test al, 8
    jnz .done
    mov si, s_search
    mov ah, INK_DIM
.done:
    ret

; DS:SI ASCIIZ in the font at BX, ink AL, from (CX, DX), on ES
draw_string:
    mov [font], bx
    mov [pen_x], cx
    mov [pen_y], dx
    mov [pen_ink], al
.next:
    lodsb
    test al, al
    jz .done
    call draw_char
    jmp .next
.done:
    ret

; AL a character, at the pen, which moves on; SI stays as it was
draw_char:
    mov bx, [font]
    cmp al, ' '
    jne .find
    mov al, [bx + 5]                ; a space, half a letter wide
    shr al, 1
    add al, [bx + 4]
    xor ah, ah
    add [pen_x], ax
    ret
.find:
    push si
    mov si, [bx + 2]
    xor cx, cx
    mov ah, al
.look:
    lodsb
    test al, al
    jz .none
    cmp al, ah
    je .found
    inc cx
    jmp .look
.none:
    pop si
    ret
.found:
    mov si, [bx]
    add si, cx
    add si, cx
    mov si, [si]
    lodsb                           ; its width
    xor ah, ah
    push ax
    mov dx, [pen_y]
.run:
    lodsb                           ; so many rows
    test al, al
    jz .end
    xor ah, ah
    mov bp, ax
    lodsb                           ; with so many spans
    mov bl, al
.span:
    test bl, bl
    jz .spans_done
    lodsb                           ; where each starts, and its length
    xor ah, ah
    add ax, [pen_x]
    mov cx, ax
    lodsb
    xor ah, ah
    push si
    mov si, ax
    mov al, [pen_ink]
    call fill_rect
    pop si
    dec bl
    jmp .span
.spans_done:
    add dx, bp
    jmp .run
.end:
    pop ax
    mov bx, [font]
    add al, [bx + 4]
    adc ah, 0
    add [pen_x], ax
    pop si
    ret

; From (CX, DX), SI wide and BP high, in the ink AL (four pixels of it),
; on ES. Every register stays as it was.
fill_rect:
    test si, si
    jz .nothing
    push ax
    push bx
    push cx
    push dx
    push si
    push di
    push bp
    mov bl, al
    mov ax, dx
    mul word [w_pitch]
    mov di, cx
    shr di, 1
    shr di, 1
    mov dx, di                      ; its first byte in the row
    add di, ax
    add si, cx
    dec si                          ; its last pixel
    and cl, 3
    shl cl, 1
    mov ch, 0FFh
    shr ch, cl
    mov [rect_left], ch
    mov cx, si
    and cl, 3
    mov ch, 3
    sub ch, cl
    mov cl, ch
    shl cl, 1
    mov ch, 0FFh
    shl ch, cl
    mov [rect_right], ch
    shr si, 1
    shr si, 1
    sub si, dx                      ; bytes from the first to the last
.row:
    push di
    mov bh, [rect_left]
    mov cx, si
    test cx, cx
    jz .last
    call .masked
    inc di
    dec cx
    mov al, bl
    rep stosb
    mov bh, 0FFh
.last:
    and bh, [rect_right]
    call .masked
    pop di
    add di, [w_pitch]
    dec bp
    jnz .row
    pop bp
    pop di
    pop si
    pop dx
    pop cx
    pop bx
    pop ax
.nothing:
    ret
.masked:
    mov al, bl
    and al, bh
    not bh
    and bh, [es:di]
    or al, bh
    mov [es:di], al
    ret

names           dw name_gun, 3, name_msl, 3, name_ir, 31, name_lsr, 3
name_gun        db 'GUN', 0
name_msl        db 'MSL', 0
name_ir         db 'IR', 0
name_lsr        db 'LSR', 0
s_ammo          db 'AMMO', 0
s_heat          db 'HEAT', 0
s_dist          db 'DIST', 0
s_no_ammo       db 'NO AMMO', 0
s_no_target     db 'NO TARGET', 0
s_interdict     db 'INTERDICT', 0
s_too_hot       db 'TOO HOT', 0
s_out_of_arc    db 'OUT OF ARC', 0
s_msl_ready     db 'READY', 0
s_launch        db 'LAUNCH', 0
s_guiding       db 'GUIDING', 0
s_no_link       db 'NO LINK', 0
s_lost          db 'MSL LOST', 0
s_hit           db 'HIT', 0
s_missed        db 'MISSED', 0
s_ir_tone       db 'IR TONE', 0
s_no_heat       db 'NO HEAT', 0
s_ir_lock       db 'IR LOCK', 0
s_search        db 'SEARCH', 0
s_head_off      db 'SEEKER OFF', 0

; ---- font tables (tools/firectl_font.py) ----
; big: 58 rows, a gap of 2; a glyph: its width, then runs: rows, spans, x and length of each
big_font dw big_glyphs, big_chars
    db 2, 34
big_glyphs:
    dw big_G
    dw big_U
    dw big_N
    dw big_M
    dw big_S
    dw big_L
    dw big_I
    dw big_R
big_chars db 'GUNMSLIR', 0
big_G:
    db 34, 1, 1, 3, 28, 1, 1, 2, 30, 1, 1, 1, 32, 7, 1, 0, 34, 1, 1, 0, 33, 1, 1, 0
    db 32, 1, 1, 0, 31, 12, 1, 0, 13, 1, 2, 0, 13, 20, 11, 1, 2, 0, 13, 19, 13, 1, 2, 0
    db 13, 18, 15, 7, 2, 0, 13, 17, 17, 1, 2, 0, 13, 18, 16, 1, 2, 0, 13, 19, 15, 1, 2, 0
    db 13, 20, 14, 7, 2, 0, 13, 21, 13, 10, 1, 0, 34, 1, 1, 1, 32, 1, 1, 2, 30, 1, 1, 3
    db 28, 0
big_U:
    db 34, 1, 2, 3, 7, 24, 7, 1, 2, 2, 9, 23, 9, 1, 2, 1, 11, 22, 11, 42, 2, 0, 13, 21
    db 13, 10, 1, 0, 34, 1, 1, 1, 32, 1, 1, 2, 30, 1, 1, 3, 28, 0
big_N:
    db 34, 1, 2, 3, 7, 24, 7, 1, 2, 2, 9, 23, 9, 1, 2, 1, 11, 22, 11, 2, 2, 0, 13, 21
    db 13, 2, 2, 0, 14, 21, 13, 2, 2, 0, 15, 21, 13, 2, 2, 0, 16, 21, 13, 2, 2, 0, 17, 21
    db 13, 2, 2, 0, 18, 21, 13, 2, 2, 0, 19, 21, 13, 3, 2, 0, 20, 21, 13, 18, 1, 0, 34, 3
    db 2, 0, 13, 14, 20, 2, 2, 0, 13, 15, 19, 2, 2, 0, 13, 16, 18, 2, 2, 0, 13, 17, 17, 2
    db 2, 0, 13, 18, 16, 2, 2, 0, 13, 19, 15, 2, 2, 0, 13, 20, 14, 2, 2, 0, 13, 21, 13, 1
    db 2, 1, 11, 22, 11, 1, 2, 2, 9, 23, 9, 1, 2, 3, 7, 24, 7, 0
big_M:
    db 34, 1, 2, 3, 7, 24, 7, 1, 2, 2, 9, 23, 9, 1, 2, 1, 11, 22, 11, 1, 2, 0, 13, 21
    db 13, 2, 2, 0, 14, 20, 14, 2, 2, 0, 15, 19, 15, 2, 2, 0, 16, 18, 16, 21, 1, 0, 34, 1
    db 2, 0, 20, 21, 13, 23, 2, 0, 13, 21, 13, 1, 2, 1, 11, 22, 11, 1, 2, 2, 9, 23, 9, 1
    db 2, 3, 7, 24, 7, 0
big_S:
    db 34, 1, 1, 3, 28, 1, 1, 2, 30, 1, 1, 1, 32, 7, 1, 0, 34, 1, 1, 0, 33, 1, 1, 0
    db 32, 1, 1, 0, 31, 9, 1, 0, 13, 1, 1, 0, 31, 1, 1, 0, 32, 1, 1, 0, 33, 7, 1, 0
    db 34, 1, 1, 1, 33, 1, 1, 2, 32, 1, 1, 3, 31, 10, 1, 21, 13, 1, 1, 3, 31, 1, 1, 2
    db 32, 1, 1, 1, 33, 7, 1, 0, 34, 1, 1, 1, 32, 1, 1, 2, 30, 1, 1, 3, 28, 0
big_L:
    db 34, 1, 1, 3, 7, 1, 1, 2, 9, 1, 1, 1, 11, 42, 1, 0, 13, 1, 1, 0, 31, 1, 1, 0
    db 32, 1, 1, 0, 33, 7, 1, 0, 34, 1, 1, 1, 32, 1, 1, 2, 30, 1, 1, 3, 28, 0
big_I:
    db 13, 1, 1, 3, 7, 1, 1, 2, 9, 1, 1, 1, 11, 52, 1, 0, 13, 1, 1, 1, 11, 1, 1, 2
    db 9, 1, 1, 3, 7, 0
big_R:
    db 34, 1, 1, 3, 28, 1, 1, 2, 30, 1, 1, 1, 32, 10, 1, 0, 34, 10, 2, 0, 13, 21, 13, 10
    db 1, 0, 34, 1, 1, 0, 33, 1, 1, 0, 32, 1, 1, 0, 31, 1, 1, 0, 27, 2, 1, 0, 28, 2
    db 1, 0, 29, 1, 1, 0, 30, 1, 2, 0, 13, 14, 16, 1, 2, 0, 13, 15, 16, 1, 2, 0, 13, 15
    db 17, 1, 2, 0, 13, 16, 16, 1, 2, 0, 13, 16, 17, 1, 2, 0, 13, 17, 16, 1, 2, 0, 13, 17
    db 17, 1, 2, 0, 13, 18, 16, 2, 2, 0, 13, 19, 15, 2, 2, 0, 13, 20, 14, 1, 2, 0, 13, 21
    db 13, 1, 2, 1, 11, 22, 11, 1, 2, 2, 9, 23, 9, 1, 2, 3, 7, 24, 7, 0
; mid: 24 rows, a gap of 2; a glyph: its width, then runs: rows, spans, x and length of each
mid_font dw mid_glyphs, mid_chars
    db 2, 14
mid_glyphs:
    dw mid_0
    dw mid_1
    dw mid_2
    dw mid_3
    dw mid_4
    dw mid_5
    dw mid_6
    dw mid_7
    dw mid_8
    dw mid_9
    dw mid_minus
    dw mid_N
    dw mid_O
    dw mid_A
    dw mid_M
mid_chars db '0123456789-NOAM', 0
mid_0:
    db 14, 1, 1, 1, 12, 4, 1, 0, 14, 14, 2, 0, 5, 9, 5, 4, 1, 0, 14, 1, 1, 1, 12, 0
mid_1:
    db 14, 1, 1, 6, 3, 1, 1, 4, 6, 1, 1, 3, 7, 1, 1, 2, 8, 3, 1, 1, 9, 1, 1, 2
    db 8, 15, 1, 5, 5, 1, 1, 6, 3, 0
mid_2:
    db 14, 1, 1, 1, 12, 4, 1, 0, 14, 2, 2, 0, 5, 9, 5, 1, 2, 1, 3, 9, 5, 1, 1, 9
    db 5, 1, 1, 1, 13, 3, 1, 0, 14, 1, 1, 0, 13, 5, 1, 0, 5, 1, 1, 0, 13, 3, 1, 0
    db 14, 1, 1, 1, 12, 0
mid_3:
    db 14, 1, 1, 1, 12, 3, 1, 0, 14, 1, 1, 1, 13, 4, 1, 9, 5, 1, 1, 4, 10, 3, 1, 3
    db 11, 1, 1, 4, 10, 5, 1, 9, 5, 1, 1, 1, 13, 3, 1, 0, 14, 1, 1, 1, 12, 0
mid_4:
    db 14, 1, 2, 1, 3, 10, 3, 10, 2, 0, 5, 9, 5, 4, 1, 0, 14, 1, 1, 1, 13, 7, 1, 9
    db 5, 1, 1, 10, 3, 0
mid_5:
    db 14, 1, 1, 1, 12, 3, 1, 0, 14, 1, 1, 0, 13, 4, 1, 0, 5, 1, 1, 0, 13, 3, 1, 0
    db 14, 1, 1, 1, 13, 5, 1, 9, 5, 1, 1, 1, 13, 3, 1, 0, 14, 1, 1, 1, 12, 0
mid_6:
    db 14, 1, 1, 1, 12, 3, 1, 0, 14, 1, 1, 0, 13, 4, 1, 0, 5, 1, 1, 0, 13, 4, 1, 0
    db 14, 5, 2, 0, 5, 9, 5, 4, 1, 0, 14, 1, 1, 1, 12, 0
mid_7:
    db 14, 1, 1, 1, 12, 3, 1, 0, 14, 1, 1, 1, 13, 2, 1, 8, 5, 2, 1, 7, 6, 2, 1, 7
    db 5, 2, 1, 6, 6, 2, 1, 6, 5, 2, 1, 5, 6, 2, 1, 5, 5, 2, 1, 4, 6, 2, 1, 4
    db 5, 1, 1, 5, 3, 0
mid_8:
    db 14, 1, 1, 1, 12, 4, 1, 0, 14, 4, 2, 0, 5, 9, 5, 5, 1, 0, 14, 5, 2, 0, 5, 9
    db 5, 4, 1, 0, 14, 1, 1, 1, 12, 0
mid_9:
    db 14, 1, 1, 1, 12, 4, 1, 0, 14, 5, 2, 0, 5, 9, 5, 4, 1, 0, 14, 1, 1, 1, 13, 4
    db 1, 9, 5, 1, 1, 1, 13, 3, 1, 0, 14, 1, 1, 1, 12, 0
mid_minus:
    db 14, 10, 0, 1, 1, 2, 10, 3, 1, 1, 12, 1, 1, 2, 10, 9, 0, 0
mid_N:
    db 14, 1, 2, 1, 3, 10, 3, 2, 2, 0, 5, 9, 5, 2, 2, 0, 6, 9, 5, 2, 2, 0, 7, 9
    db 5, 2, 2, 0, 8, 9, 5, 6, 1, 0, 14, 2, 2, 0, 5, 6, 8, 2, 2, 0, 5, 7, 7, 2
    db 2, 0, 5, 8, 6, 2, 2, 0, 5, 9, 5, 1, 2, 1, 3, 10, 3, 0
mid_O:
    db 14, 1, 1, 1, 12, 4, 1, 0, 14, 14, 2, 0, 5, 9, 5, 4, 1, 0, 14, 1, 1, 1, 12, 0
mid_A:
    db 14, 1, 1, 1, 12, 4, 1, 0, 14, 5, 2, 0, 5, 9, 5, 5, 1, 0, 14, 8, 2, 0, 5, 9
    db 5, 1, 2, 1, 3, 10, 3, 0
mid_M:
    db 14, 1, 2, 1, 3, 10, 3, 1, 2, 0, 5, 9, 5, 2, 2, 0, 6, 8, 6, 8, 1, 0, 14, 1
    db 2, 0, 8, 9, 5, 10, 2, 0, 5, 9, 5, 1, 2, 1, 3, 10, 3, 0
; small: 13 rows, a gap of 1; a glyph: its width, then runs: rows, spans, x and length of each
small_font dw small_glyphs, small_chars
    db 1, 9
small_glyphs:
    dw small_A
    dw small_C
    dw small_D
    dw small_E
    dw small_F
    dw small_G
    dw small_H
    dw small_I
    dw small_K
    dw small_L
    dw small_M
    dw small_N
    dw small_O
    dw small_R
    dw small_S
    dw small_T
    dw small_U
    dw small_Y
small_chars db 'ACDEFGHIKLMNORSTUY', 0
small_A:
    db 9, 1, 1, 1, 7, 2, 1, 0, 9, 2, 2, 0, 3, 6, 3, 3, 1, 0, 9, 4, 2, 0, 3, 6
    db 3, 1, 2, 1, 1, 7, 1, 0
small_C:
    db 9, 1, 1, 1, 7, 2, 1, 0, 9, 1, 2, 0, 3, 6, 3, 1, 2, 0, 3, 7, 1, 3, 1, 0
    db 3, 1, 2, 0, 3, 7, 1, 1, 2, 0, 3, 6, 3, 2, 1, 0, 9, 1, 1, 1, 7, 0
small_D:
    db 9, 1, 1, 1, 5, 1, 1, 0, 7, 1, 1, 0, 8, 1, 2, 0, 3, 5, 3, 5, 2, 0, 3, 6
    db 3, 1, 2, 0, 3, 5, 3, 1, 1, 0, 8, 1, 1, 0, 7, 1, 1, 1, 5, 0
small_E:
    db 9, 1, 1, 1, 7, 1, 1, 0, 9, 1, 1, 0, 8, 2, 1, 0, 3, 1, 1, 0, 7, 1, 1, 0
    db 8, 1, 1, 0, 7, 2, 1, 0, 3, 1, 1, 0, 8, 1, 1, 0, 9, 1, 1, 1, 7, 0
small_F:
    db 9, 1, 1, 1, 7, 1, 1, 0, 9, 1, 1, 0, 8, 2, 1, 0, 3, 1, 1, 0, 7, 1, 1, 0
    db 8, 1, 1, 0, 7, 4, 1, 0, 3, 1, 1, 1, 1, 0
small_G:
    db 9, 1, 1, 1, 7, 1, 1, 0, 9, 1, 1, 0, 8, 2, 1, 0, 3, 1, 2, 0, 3, 6, 2, 1
    db 2, 0, 3, 5, 4, 3, 2, 0, 3, 6, 3, 2, 1, 0, 9, 1, 1, 1, 7, 0
small_H:
    db 9, 1, 2, 1, 1, 7, 1, 4, 2, 0, 3, 6, 3, 3, 1, 0, 9, 4, 2, 0, 3, 6, 3, 1
    db 2, 1, 1, 7, 1, 0
small_I:
    db 3, 1, 1, 1, 1, 11, 1, 0, 3, 1, 1, 1, 1, 0
small_K:
    db 9, 1, 2, 1, 1, 7, 1, 1, 2, 0, 3, 6, 3, 1, 2, 0, 3, 5, 3, 1, 1, 0, 7, 1
    db 1, 0, 6, 1, 1, 0, 5, 2, 1, 0, 6, 1, 2, 0, 3, 4, 3, 2, 2, 0, 3, 5, 3, 1
    db 2, 0, 3, 6, 3, 1, 2, 1, 1, 7, 1, 0
small_L:
    db 9, 1, 1, 1, 1, 9, 1, 0, 3, 1, 1, 0, 8, 1, 1, 0, 9, 1, 1, 1, 7, 0
small_M:
    db 9, 1, 2, 1, 1, 7, 1, 1, 2, 0, 3, 6, 3, 1, 2, 0, 4, 5, 4, 3, 1, 0, 9, 1
    db 3, 0, 3, 4, 1, 6, 3, 5, 2, 0, 3, 6, 3, 1, 2, 1, 1, 7, 1, 0
small_N:
    db 9, 1, 2, 1, 1, 7, 1, 1, 2, 0, 3, 6, 3, 2, 2, 0, 4, 6, 3, 2, 2, 0, 5, 6
    db 3, 1, 1, 0, 9, 2, 2, 0, 3, 4, 5, 2, 2, 0, 3, 5, 4, 1, 2, 0, 3, 6, 3, 1
    db 2, 1, 1, 7, 1, 0
small_O:
    db 9, 1, 1, 1, 7, 2, 1, 0, 9, 7, 2, 0, 3, 6, 3, 2, 1, 0, 9, 1, 1, 1, 7, 0
small_R:
    db 9, 1, 1, 1, 7, 2, 1, 0, 9, 2, 2, 0, 3, 6, 3, 2, 1, 0, 9, 1, 1, 0, 8, 2
    db 2, 0, 3, 4, 3, 1, 2, 0, 3, 5, 3, 1, 2, 0, 3, 6, 3, 1, 2, 1, 1, 7, 1, 0
small_S:
    db 9, 1, 1, 1, 7, 1, 1, 0, 9, 1, 1, 0, 8, 2, 1, 0, 3, 1, 1, 0, 8, 1, 1, 0
    db 9, 1, 1, 1, 8, 2, 1, 6, 3, 1, 1, 1, 8, 1, 1, 0, 9, 1, 1, 1, 7, 0
small_T:
    db 9, 1, 1, 1, 7, 1, 1, 0, 9, 1, 1, 1, 7, 9, 1, 3, 3, 1, 1, 4, 1, 0
small_U:
    db 9, 1, 2, 1, 1, 7, 1, 9, 2, 0, 3, 6, 3, 2, 1, 0, 9, 1, 1, 1, 7, 0
small_Y:
    db 9, 1, 2, 1, 1, 7, 1, 1, 2, 0, 3, 6, 3, 2, 2, 1, 3, 5, 3, 2, 1, 2, 5, 6
    db 1, 3, 3, 1, 1, 4, 1, 0
; ---- end of the font tables ----

font            dw 0                ; the pen: the font, where, its ink
pen_x           dw 0
pen_y           dw 0
pen_ink         db 0
rect_left       db 0                ; the masks of a rectangle's ends
rect_right      db 0
digits          db 0, 0, 0, 0, 0
shown_ammo      db 0, 0, 0, 0, 0    ; what the page shows now
shown_dist      db 0, 0, 0, 0, 0
shown_slot      dw 0

buttons         db 0
pressed         db 0
locked          db 0
firing          db 0
rounds_left     db 0
screen_wait     db 1
turret_status   db 0
has_launcher    db 0
has_laser       db 0
has_interdictor db 0
interdict_button db 0
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
r_entry         dw 0, 0             ; its entry in the screen table
r_lent          db 0                ; another program has it
r_w             dw 0                ; its size in pixels
r_h             dw 0
r_pitch         dw 0                ; bytes a row
r_words         dw 0                ; words in all
w_seg           dw 0                ; the WEAPON screen's segment (0: none)
w_pitch         dw 0                ; bytes a row
w_words         dw 0                ; words in all
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
    mov dx, INTERDICTOR
    in al, dx
    cmp al, TYPE_INTERDICTOR
    jne .no_interdictor
    mov byte [has_interdictor], 1
.no_interdictor:

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
    mov [r_entry], bx
    mov [r_entry + 2], es
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
    cmp word [es:bx + 10], PAGE_MIN_W
    jb .no_page
    cmp word [es:bx + 12], PAGE_MIN_H
    jb .no_page
    mov byte [es:bx + 6], 1         ; graphics
    mov ax, [es:bx + 10]
    shr ax, 1
    shr ax, 1
    mov [w_pitch], ax
    mul word [es:bx + 12]
    inc ax
    shr ax, 1
    mov [w_words], ax
    call screen_segment
    mov [w_seg], ax
    call draw_frame
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
    mov ax, [es:w_pitch]
    mov [w_pitch], ax
    mov ax, [es:w_words]
    mov [w_words], ax
    mov ax, [es:r_seg]
    mov [r_seg], ax
    mov ax, [es:r_words]
    mov [r_words], ax
    push es
    cmp word [w_seg], 0
    je .no_page
    mov es, [w_seg]
    xor di, di
    xor ax, ax
    mov cx, [w_words]
    rep stosw
    mov si, s_removed
    mov cx, PAGE_LEFT
    mov dx, STATUS_Y
    mov bx, small_font
    mov al, INK_DIM
    call draw_string
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

n_radar         db 'RADAR '
n_weapon        db 'WEAPON'

s_removed       db 'FIRECTL OFF', 0
msg_installed   db 'Fire control resident: scope on RADAR, page on WEAPON.', 13, 10
                db 'FIRECTL OFF removes it.', 13, 10, '$'
msg_already     db 'Fire control is already resident.', 13, 10, '$'
msg_not_running db 'Fire control is not resident, or a program that is not', 13, 10
                db "one of the ship's took IRQ 5 after it.", 13, 10, '$'
msg_removed     db 'Fire control removed: radar off.', 13, 10, '$'
msg_no_device   db 'FIRECTL: the radar, the turret or the side screens', 13, 10
                db 'are not on the bus.', 13, 10, '$'
