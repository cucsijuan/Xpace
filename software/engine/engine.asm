; engine.asm - CucsiCorp Drive Controller 1.0 (ENGINE.BIN, firmware)
;
; The program in the main engine's own controller: a bare 8086 with no
; BIOS and no DOS, built into the engine, not part of the ship's computer.
; It starts at 100h, points the clock's interrupt at its handler and waits
; in HLT; the clock ticks sixty times a second.
;
; The ship asks for thrust; the controller decides what the engine gives:
;
;   - the thrust asked for, times the trim (the shipyard's setting: 1 is
;     the engine's rating);
;   - nothing while the core is too hot. The core has no thermometer: its
;     temperature is worked out from the coolant loop's, which the cooling
;     unit reports, and the thrust the engine was giving, as the coolant's
;     plus K for each rating's worth of thrust. Past CUT_OUT it switches
;     the engine off, and on again once it is down to RESUME;
;   - the core is looked at only every SAMPLE_TICKS ticks; in between, the
;     last decision stands.
;
; Service: the shipyard talks to the controller through the service port,
; a byte at a time.
;
; The ports (words little-endian):
;
;   100h  +0 thrust asked for (0 to 255, the rating)
;         +1 service: counts up with every byte the ship sends
;         +2 service: the last byte
;         +4 coolant temperature, as the cooling unit reports it (1/4 C)
;         +6 write: thrust given (1/256 of the rating)
;         +8 write: status, bit 0 cut out, bit 2 bench mode
;         +10 write: core temperature worked out (1/4 C)
;         +12 write: cut-out temperature  +14 write: resume temperature

org 100h

PORTS       equ 100h
P_WANTED    equ PORTS + 0
P_SERVICE_N equ PORTS + 1
P_SERVICE   equ PORTS + 2
P_COOLANT   equ PORTS + 4
P_GIVEN     equ PORTS + 6
P_STATUS    equ PORTS + 8
P_CORE      equ PORTS + 10
P_CUT_OUT   equ PORTS + 12
P_RESUME    equ PORTS + 14
TICK_VECTOR equ 08h

CUT_OUT     equ 1000                ; 250 C, in 1/4 C
RESUME      equ 800                 ; 200 C
K_CORE      equ 320                 ; 80 C over the coolant at the rating
SAMPLE_TICKS equ 15                 ; four looks a second
TRIM_RATED  equ 256

ST_CUT      equ 1
ST_BENCH    equ 4

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
    push ds
    push cs
    pop ds
    call service
    call control
    pop ds
    pop si
    pop dx
    pop cx
    pop bx
    pop ax
    iret

; Every tick: what the engine gives; every SAMPLE_TICKS, a look at the core
control:
    dec byte [countdown]
    jnz .give
    mov byte [countdown], SAMPLE_TICKS
    call core                       ; AX = the core's temperature, worked out
    mov dx, P_CORE
    out dx, ax
    test byte [status], ST_BENCH
    jnz .give                       ; on the bench the shop cools it: no cut-out
    cmp ax, CUT_OUT
    jl .not_hot
    or byte [status], ST_CUT
.not_hot:
    cmp ax, RESUME
    jg .give
    and byte [status], 0FFh - ST_CUT
.give:
    xor ax, ax
    test byte [status], ST_CUT
    jnz .out
    mov dx, P_WANTED
    in al, dx
    xor ah, ah
    mul word [trim]                 ; DX:AX = asked x trim, in 1/256
    mov al, ah                      ; / 256: the given thrust in 1/256 of the rating
    mov ah, dl
.out:
    mov [given], ax
    mov dx, P_GIVEN
    out dx, ax
    mov al, [status]
    mov dx, P_STATUS
    out dx, al
    mov ax, CUT_OUT
    mov dx, P_CUT_OUT
    out dx, ax
    mov ax, RESUME
    mov dx, P_RESUME
    out dx, ax
    ret

; AX = the coolant's temperature + K_CORE for each rating's worth of the
; thrust given now
core:
    mov ax, [given]
    mov cx, K_CORE
    mul cx
    mov al, ah                      ; / 256
    mov ah, dl
    mov bx, ax
    mov dx, P_COOLANT
    in ax, dx
    add ax, bx
    ret

; A new byte on the service port: the shop's sequence opens the bench mode,
; where 'T' and a byte set the trim (1 + byte / 256) and 'X' closes it
service:
    mov dx, P_SERVICE_N
    in al, dx
    cmp al, [service_seen]
    je .done
    mov [service_seen], al
    mov dx, P_SERVICE
    in al, dx
    test byte [status], ST_BENCH
    jnz .bench
    mov bl, [unlock_at]
    xor bh, bh
    cmp al, [unlock + bx]
    je .next
    mov byte [unlock_at], 0
    cmp al, [unlock]                ; it may be the start of the sequence again
    jne .done
    mov byte [unlock_at], 1
    ret
.next:
    inc byte [unlock_at]
    cmp byte [unlock_at], UNLOCK_LENGTH
    jb .done
    mov byte [unlock_at], 0
    or byte [status], ST_BENCH
    and byte [status], 0FFh - ST_CUT
    ret
.bench:
    cmp byte [trim_next], 0
    je .command
    mov byte [trim_next], 0
    xor ah, ah
    add ax, TRIM_RATED
    mov [trim], ax
    ret
.command:
    cmp al, 'T'
    jne .close
    mov byte [trim_next], 1
    ret
.close:
    cmp al, 'X'
    jne .done
    and byte [status], 0FFh - ST_BENCH
    mov word [trim], TRIM_RATED
.done:
    ret

UNLOCK_LENGTH equ 4
unlock      db 0A7h, 1Ch, 0E5h, 52h

trim        dw TRIM_RATED
given       dw 0
status      db 0
countdown   db 1
service_seen db 0
unlock_at   db 0
trim_next   db 0
