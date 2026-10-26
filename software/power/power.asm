; power.asm - CucsiCorp Power Manager 1.1 (POWER.COM)
;
;   POWER       stays resident and works the power distributor and the
;               heat sinks from the pilot's buttons
;   POWER OFF   takes it out of memory
;
; The distributor shares the power plant among systems (the shields),
; engines (the thrusters) and weapons in twelve half pips, at most eight
; on one. A press of the systems, engines or weapons button gives that one
; a pip (two halves): one half from each of the others, or both from the
; one that has some, as far as it can take (eight). The fourth button
; evens them out: four halves each. The heat sink button throws a heat
; sink, if the ship has a heat sink launcher.
;
; Sixty times a second, on IRQ 5 (after the handlers installed before it),
; it looks at the buttons; on a press it reads the distributor's setting,
; changes it and writes it back (all three, the weapons last: that sets
; them).
;
; The devices (16 ports each):
;
;   100h  pilot's controls   +15 buttons: bit 1 systems, 2 engines,
;                            3 weapons, 4 even out, 5 heat sink
;   1B0h  power distributor  +0 type 15  +2 systems  +3 engines  +4 weapons
;                            (half pips; writing +4 sets the three)
;   1D0h  heat               +0 type 14  +4 write: bit 0 a heat sink

org 100h

CTRL        equ 100h
POWER       equ 1B0h
HEAT        equ 1D0h
TYPE_POWER  equ 15
TYPE_HEAT   equ 14
BUTTON_SYS  equ 2                   ; (bit 0 is FLIGHT's safe turns)
BUTTON_ENG  equ 4
BUTTON_WEP  equ 8
BUTTON_EVEN equ 16
BUTTON_SINK equ 32
MOST        equ 8                   ; half pips on one
IRQ_VECTOR  equ 0Dh
IRQ_BIT     equ 20h

    jmp start

; ======================================================================
; The resident part
; ======================================================================

signature   db 'XPPW11'             ; 103h
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
    push ds
    push cs
    pop ds
    call step
    pop ds
    pop di
    pop si
    pop dx
    pop cx
    pop bx
    pop ax
    iret

step:
    mov dx, CTRL + 15
    in al, dx
    mov ah, [buttons]
    mov [buttons], al
    not ah
    and al, ah                      ; pressed since the last look
    jz .done
    mov [pressed], al
    test al, BUTTON_SINK
    jz .pips
    cmp byte [heat_present], 0
    je .pips
    mov al, 1
    mov dx, HEAT + 4
    out dx, al
.pips:
    test byte [pressed], BUTTON_SYS | BUTTON_ENG | BUTTON_WEP | BUTTON_EVEN
    jz .done
    mov dx, POWER + 2               ; the setting now
    in al, dx
    mov [pips], al
    inc dx
    in al, dx
    mov [pips + 1], al
    inc dx
    in al, dx
    mov [pips + 2], al
    test byte [pressed], BUTTON_EVEN
    jz .sys
    mov byte [pips], 4
    mov byte [pips + 1], 4
    mov byte [pips + 2], 4
    jmp .write
.sys:
    test byte [pressed], BUTTON_SYS
    jz .eng
    xor bx, bx
    call more
.eng:
    test byte [pressed], BUTTON_ENG
    jz .wep
    mov bx, 1
    call more
.wep:
    test byte [pressed], BUTTON_WEP
    jz .write
    mov bx, 2
    call more
.write:
    mov dx, POWER + 2
    mov al, [pips]
    out dx, al
    inc dx
    mov al, [pips + 1]
    out dx, al
    inc dx
    mov al, [pips + 2]
    out dx, al
.done:
    ret

; A pip more for the store BX (0 systems, 1 engines, 2 weapons): two half
; pips, each from whichever of the others has more (the first of them on
; the first half, the second on the second, when they have as many)
more:
    mov cx, 2
.half:
    cmp byte [pips + bx], MOST
    jae .done
    mov si, bx                      ; SI and DI: the other two, in turn
    inc si
    cmp si, 3
    jb .second
    xor si, si
.second:
    mov di, si
    inc di
    cmp di, 3
    jb .pick
    xor di, di
.pick:
    mov al, [pips + si]
    mov ah, [pips + di]
    cmp al, ah
    ja .take
    jb .other
    or al, al
    jz .done                        ; neither has any
    cmp cx, 2
    je .take
.other:
    mov si, di
.take:
    dec byte [pips + si]
    inc byte [pips + bx]
    loop .half
.done:
    ret

buttons     db 0
pressed     db 0
heat_present db 0
pips        db 4, 4, 4

resident_end:

; ======================================================================
; Installing and removing
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
    mov dx, POWER
    in al, dx
    cmp al, TYPE_POWER
    je .power
    mov dx, msg_no_device
    jmp quit_message
.power:
    mov dx, HEAT                    ; heat sinks to throw?
    in al, dx
    cmp al, TYPE_HEAT
    jne .no_heat
    mov byte [heat_present], 1
.no_heat:
    mov dx, CTRL + 15
    in al, dx
    mov [buttons], al

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

quit_message:
    mov ah, 09h
    int 21h
    mov ax, 4C01h
    int 21h

msg_installed   db 'Power manager resident.', 13, 10, '$'
msg_already     db 'Power manager is already resident.', 13, 10, '$'
msg_not_running db 'Power manager is not resident, or a program that is not', 13, 10
                db "one of the ship's took IRQ 5 after it.", 13, 10, '$'
msg_removed     db 'Power manager removed.', 13, 10, '$'
msg_no_device   db 'POWER: the ship has no power distributor.', 13, 10, '$'
