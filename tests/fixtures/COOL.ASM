; COOL.ASM - throws a heat sink when the ship runs hot
;
;   COOL        stays resident
;   COOL OFF    takes it out of memory

org 100h

HEAT        equ 1D0h
TYPE_HEAT   equ 14
TOO_HOT     equ 100                 ; percent
IRQ_VECTOR  equ 0Dh
IRQ_BIT     equ 20h

    jmp near start

; ---------- the resident part ----------

signature   db 'XPCOOL'             ; 103h
old_irq     dw 0, 0                 ; 109h

on_irq:
    pushf
    call far [cs:old_irq]           ; the programs before this one first
    push ax
    push dx
    mov dx, HEAT + 5                ; a sink already working?
    in al, dx
    test al, 1
    jnz .done
    mov dx, HEAT + 2
    in al, dx
    cmp al, TOO_HOT
    jb .done
    mov dx, HEAT + 4                ; throw one
    mov al, 1
    out dx, al
.done:
    pop dx
    pop ax
    iret

resident_end:

; ---------- installing and removing ----------

start:
    mov si, 81h
.blank:
    lodsb
    cmp al, ' '
    je .blank
    and al, 0DFh
    cmp al, 'O'
    je remove

install:
    call find_resident
    jne .new
    mov dx, msg_already
    jmp quit_message
.new:
    mov dx, HEAT
    in al, dx
    cmp al, TYPE_HEAT
    je .hook
    mov dx, msg_no_device
    jmp quit_message
.hook:
    mov ax, 3500h + IRQ_VECTOR
    int 21h
    mov [old_irq], bx
    mov [old_irq + 2], es
    mov dx, on_irq
    mov ax, 2500h + IRQ_VECTOR
    int 21h
    in al, 21h                      ; IRQ 5 on at the interrupt controller
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
    push ds                         ; out of the chain
    cli
    mov ax, [es:old_irq]
    mov dx, [es:old_irq + 2]
    cmp word [prev_seg], 0
    je .first
    mov ds, [prev_seg]              ; the program before calls what we called
    mov [109h], ax
    mov [10Bh], dx
    jmp .unlinked
.first:
    mov ds, dx                      ; or the vector does
    mov dx, ax
    mov ax, 2500h + IRQ_VECTOR
    int 21h
.unlinked:
    sti
    pop ds
    mov ah, 49h                     ; free the resident copy
    int 21h
    mov dx, msg_removed
    jmp quit_message

; ZF set and ES the resident copy if it is on the chain; [prev_seg] the
; program before it (0: it is first)
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
    cmp word [es:103h], 'XP'        ; another ship's program: go on
    jne .no
    mov [prev_seg], es
    les bx, [es:109h]
    mov ax, es
    or ax, bx
    jnz .next
.no:
    or al, 1                        ; ZF = 0
.done:
    ret

quit_message:
    mov ah, 09h
    int 21h
    mov ax, 4C00h
    int 21h

prev_seg        dw 0
msg_installed   db 'COOL resident.', 13, 10, '$'
msg_already     db 'COOL is already resident.', 13, 10, '$'
msg_not_running db 'COOL is not resident.', 13, 10, '$'
msg_removed     db 'COOL removed.', 13, 10, '$'
msg_no_device   db 'COOL: no heat sink launcher on the bus.', 13, 10, '$'
