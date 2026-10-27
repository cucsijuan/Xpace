; comms.asm - Radio Contacts 1.1 (COMMS.COM)
;
;   COMMS       stays resident and works the radio from the ship's COMMS
;               screen (32 x 12 text) and its keys
;   COMMS OFF   takes it out of memory
;
; The screen lists the radio's contacts, nearest first: callsign, station
; or ship, range, and LAWS beside the station whose laws the radio holds
; (DOCK if it cleared the ship to a dock). The screen's keys move a cursor
; along the list (UP, DOWN) and choose the contact under it (SELECT): its
; options come up, and SELECT on one sends it (BACK goes back to the list).
; A station can be asked for a dock or for its laws; a ship, nothing yet.
; The keys of the screen of interest (zoom in, zoom out, sound) work as UP,
; DOWN and SELECT.
;
; Below the list: the last message the radio heard, who sent it and its
; words, and the hangar the ship is cleared to. The bottom line says what
; the laws are where the ship is: the speed limit and whose it is,
; PROTECTED ZONE inside a station's zone but outside its speed limit,
; OPEN SPACE outside the zone of the laws held, NO LAWS RECEIVED with none.
;
; On IRQ 5 (after the handlers installed before it) it looks at the keys
; every time and draws four times a second, or at once after a key.
;
; The devices (16 ports each):
;
;   150h  display card       +0 type 6   +2 segment  +4 screen table
;                            +6 screens  +8 keys pressed so far  +9 the
;                            last key (1 zoom in, 2 zoom out, 3 sound, 4 up,
;                            5 down, 6 select, 7 back)  +10 its screen;
;                            a screen (16 bytes): +0 name  +6 mode (0 text)
;                            +7 columns  +8 rows  +14 memory (offset)
;   200h  radio              +0 type 18  +2 contacts  +4 status (bit 0 it
;                            holds a station's laws, bit 1 inside their
;                            speed limit, bit 3 off, bit 4 inside their
;                            zone, bit 5 cleared to a dock)  +5 write: a
;                            request (1 laws, 2 docking) to the contact in
;                            +6  +8 segment  +10 the contacts' table
;                            +12 the laws  +14 the messages (offsets).
;                            A contact (16 bytes): +0 callsign (8
;                            characters)  +8 kind ('S' station, 'V' vessel)
;                            +9 flags (bit 0 the laws held are its, bit 3 it
;                            cleared the ship to a dock)  +10 range (10 m).
;                            The laws: +0 the station's callsign  +8 speed
;                            limit (1/8 m/s)  +15 the dock cleared to.
;                            A message (128 bytes, the newest first): +0
;                            who sent it  +8 the words (120 characters)

org 100h

DISPLAYS    equ 150h
RADIO       equ 200h
TYPE_DISPLAYS equ 6
TYPE_RADIO  equ 18
IRQ_VECTOR  equ 0Dh
IRQ_BIT     equ 20h

COLS        equ 32
ROWS        equ 12
SHOWN       equ 5                   ; contacts listed at a time
FIRST_ROW   equ 2
MSG_ROW     equ 7                   ; who sent the last message; its words below
MSG_ROWS    equ 3
WRAP        equ 30                  ; the words' columns
LAWS_ROW    equ 11
MSG_SIZE    equ 128
DRAW_EVERY  equ 15                  ; steps between redraws (4 a second)
OPTIONS     equ 2                   ; a station's: docking, laws
KEY_UP      equ 4
KEY_DOWN    equ 5
KEY_SELECT  equ 6
KEY_BACK    equ 7
ASK_LAWS    equ 1
ASK_DOCKING equ 2
ATTR_LABEL  equ 03h
ATTR_VALUE  equ 0Fh
ATTR_TITLE  equ 1Fh
ATTR_STATION equ 0Ah
ATTR_LAWS   equ 0Eh
ATTR_OFF    equ 0Ch
ATTR_CURSOR equ 70h

    jmp start

; ======================================================================
; The resident part
; ======================================================================

signature   db 'XPCM11'             ; 103h
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
    push es
    push cs
    pop ds
    push cs
    pop es
    cld
    call keys
    jc .now                         ; a key: show it at once
    dec byte [countdown]
    jnz .later
.now:
    mov byte [countdown], DRAW_EVERY
    call draw
.later:
    pop es
    pop ds
    pop di
    pop si
    pop dx
    pop cx
    pop bx
    pop ax
    iret

; ---------- the keys ----------

; A key pressed for this screen since the last look: CF set if there was one
keys:
    mov dx, DISPLAYS + 8
    in al, dx
    cmp al, [key_count]
    je .none
    mov [key_count], al
    mov dx, DISPLAYS + 10
    in al, dx
    cmp al, [my_screen]
    jne .none
    dec dx
    in al, dx
    call press
    stc
    ret
.none:
    clc
    ret

; AL the key
press:
    cmp al, KEY_UP                  ; zoom in, zoom out, sound: up, down, select
    jae .mapped
    add al, KEY_UP - 1
.mapped:
    cmp byte [mode], 0
    jne .options
    cmp al, KEY_UP                  ; the list
    jne .list_down
    cmp byte [cursor], 0
    je .done
    dec byte [cursor]
    jmp remember
.list_down:
    cmp al, KEY_DOWN
    jne .list_select
    mov ah, [cursor]
    inc ah
    cmp ah, [count]
    jae .done
    mov [cursor], ah
    jmp remember
.list_select:
    cmp al, KEY_SELECT
    jne .done
    cmp byte [count], 0
    je .done
    call remember
    mov byte [mode], 1
    mov byte [option], 0
    ret
.options:
    cmp al, KEY_BACK
    je .to_list
    cmp al, KEY_UP
    jne .option_down
    cmp byte [option], 0
    je .done
    dec byte [option]
    ret
.option_down:
    cmp al, KEY_DOWN
    jne .option_select
    cmp byte [option], OPTIONS - 1
    jae .done
    inc byte [option]
    ret
.option_select:
    cmp al, KEY_SELECT
    jne .done
    cmp byte [sel_kind], 'S'        ; a ship: nothing to send yet
    jne .to_list
    mov dx, RADIO + 6               ; to the contact under the cursor
    mov al, [cursor]
    out dx, al
    dec dx
    mov al, ASK_DOCKING             ; option 0 docking, 1 laws
    sub al, [option]
    out dx, al
.to_list:
    mov byte [mode], 0
.done:
    ret

; The contact under the cursor: its callsign and kind into sel_name, sel_kind
remember:
    push ds
    mov dx, RADIO + 10
    in ax, dx
    mov si, ax
    mov al, [cursor]
    mov ah, 16
    mul ah
    add si, ax
    mov di, sel_name
    mov ds, [table_seg]
    mov cx, 9
    rep movsb
    pop ds
    ret

; The cursor follows the remembered contact as the list's order changes; one
; gone leaves the cursor where it was, within the list
follow:
    push es
    mov al, [count]
    test al, al
    jz .empty
    mov dx, RADIO + 10
    in ax, dx
    mov bx, ax
    mov es, [table_seg]
    xor cx, cx
.next:
    mov si, sel_name
    mov di, bx
    push cx
    mov cx, 8
    repe cmpsb
    pop cx
    je .found
    add bx, 16
    inc cx
    cmp cl, [count]
    jb .next
    mov al, [count]                 ; gone
    dec al
    cmp [cursor], al
    jbe .kept
    mov [cursor], al
.kept:
    mov byte [mode], 0
    pop es
    jmp remember
.found:
    mov [cursor], cl
    pop es
    ret
.empty:
    mov byte [cursor], 0
    mov byte [mode], 0
    pop es
    ret

; ---------- the screen ----------

draw:
    mov dx, RADIO + 2
    in al, dx
    cmp al, 16
    jbe .counted
    mov al, 16
.counted:
    mov [count], al
    mov dx, RADIO + 4
    in al, dx
    mov [status], al
    mov dx, RADIO + 8
    in ax, dx
    mov [table_seg], ax
    call follow

    mov es, [screen_seg]
    mov di, FIRST_ROW * COLS * 2    ; clear all but the title and the heading
    mov cx, (ROWS - FIRST_ROW) * COLS
    mov ax, (ATTR_LABEL << 8) | ' '
    rep stosw
    cmp byte [mode], 0
    jne .options
    call draw_list
    jmp .rest
.options:
    call draw_options
.rest:
    call draw_message
    jmp draw_laws

draw_list:
    mov al, [cursor]                ; the first shown keeps the cursor in sight
    sub al, SHOWN - 1
    jnc .top
    xor al, al
.top:
    mov [shown], al
    mov cl, [count]
    sub cl, al
    xor ch, ch
    cmp cx, SHOWN
    jbe .many
    mov cx, SHOWN
.many:
    mov dx, RADIO + 10
    in ax, dx
    mov si, ax
    mov al, [shown]
    mov ah, 16
    mul ah
    add si, ax
    mov bx, FIRST_ROW * COLS * 2
.contact:
    jcxz .done
    call fetch
    push cx
    push si
    call show_contact
    mov al, [shown]
    cmp al, [cursor]
    jne .plain
    lea di, [bx + 1]                ; the cursor's row: inverse
    mov cx, COLS
.bar:
    mov byte [es:di], ATTR_CURSOR
    add di, 2
    loop .bar
.plain:
    pop si
    pop cx
    inc byte [shown]
    add bx, COLS * 2
    dec cx
    jmp .contact
.done:
    ret

; The contact in entry, on the row at ES:BX
show_contact:
    lea di, [bx + 1 * 2]
    mov ah, ATTR_VALUE
    cmp byte [entry + 8], 'S'
    jne .name
    mov ah, ATTR_STATION
.name:
    call put_name
    lea di, [bx + 11 * 2]
    mov si, s_ship
    cmp byte [entry + 8], 'S'
    jne .kind
    mov si, s_station
.kind:
    mov ah, ATTR_LABEL
    call put_text
    lea di, [bx + 18 * 2]
    mov ax, [entry + 10]
    call put_km
    lea di, [bx + 27 * 2]
    mov ah, ATTR_LAWS
    mov si, s_dock
    test byte [entry + 9], 8
    jnz .flag
    mov si, s_laws
    test byte [entry + 9], 1
    jz .done
.flag:
    call put_text
.done:
    ret

; The chosen contact and what can be sent to it
draw_options:
    mov di, (FIRST_ROW * COLS + 1) * 2
    mov si, sel_name
    mov cx, 8
    mov ah, ATTR_STATION
.name:
    lodsb
    stosw
    loop .name
    add di, 2 * 2
    mov si, s_ship
    cmp byte [sel_kind], 'S'
    jne .kind
    mov si, s_station
.kind:
    mov ah, ATTR_LABEL
    call put_text
    cmp byte [sel_kind], 'S'
    je .station
    mov di, ((FIRST_ROW + 2) * COLS + 1) * 2
    mov si, s_no_options
    mov ah, ATTR_LABEL
    call put_text
    jmp .hint
.station:
    mov di, ((FIRST_ROW + 1) * COLS + 1) * 2
    mov si, s_option0
    xor dl, dl
    call put_option
    mov di, ((FIRST_ROW + 2) * COLS + 1) * 2
    mov si, s_option1
    mov dl, 1
    call put_option
.hint:
    mov di, ((FIRST_ROW + 4) * COLS + 1) * 2
    mov si, s_hint
    mov ah, ATTR_LABEL
    jmp put_text

; DS:SI an option's words, DL its number, at ES:DI
put_option:
    mov ah, ATTR_VALUE
    cmp dl, [option]
    jne .text
    mov ah, ATTR_CURSOR
.text:
    jmp put_text

; The last message: who sent it, its words wrapped on whole words
draw_message:
    mov dx, RADIO + 14
    in ax, dx
    mov si, ax
    push es
    push ds
    push cs
    pop es
    mov di, msg
    mov ds, [table_seg]
    mov cx, MSG_SIZE / 2
    rep movsw
    pop ds
    pop es
    cmp byte [msg], ' '             ; no one: nothing heard yet
    je .done
    mov di, (MSG_ROW * COLS + 1) * 2
    mov si, msg
    mov cx, 8
    mov ah, ATTR_LAWS
.from:
    lodsb
    stosw
    loop .from
    mov si, msg + 8
    mov bx, ((MSG_ROW + 1) * COLS + 1) * 2
    mov dx, MSG_ROWS
.row:
    cmp si, msg + MSG_SIZE
    jae .done
    cmp byte [si], ' '              ; no spaces at a row's start
    jne .start
    inc si
    jmp .row
.start:
    mov cx, msg + MSG_SIZE
    sub cx, si
    cmp cx, WRAP
    jbe .take                       ; the rest fits
    mov cx, WRAP
    cmp byte [si + WRAP], ' '
    je .take                        ; a whole word ends at the edge
    mov di, si                      ; back to the last space
    add di, WRAP - 1
.back:
    cmp di, si
    je .take                        ; one word longer than the row: cut it
    cmp byte [di], ' '
    je .space
    dec di
    jmp .back
.space:
    mov cx, di
    sub cx, si
.take:
    mov di, bx
    mov ah, ATTR_VALUE
.char:
    lodsb
    stosw
    loop .char
    add bx, COLS * 2
    dec dx
    jnz .row
.done:
    ret

; The bottom line: the laws where the ship is; and the dock it is cleared to
draw_laws:
    test byte [status], 32
    jz .no_dock
    mov dx, RADIO + 12
    in ax, dx
    mov si, ax
    call fetch
    mov di, (MSG_ROW * COLS + 20) * 2
    mov si, s_hangar
    mov ah, ATTR_STATION
    call put_text
    mov al, [entry + 15]
    xor ah, ah
    call put_uint
.no_dock:
    mov di, (LAWS_ROW * COLS + 1) * 2
    mov al, [status]
    test al, 8
    jz .on
    mov si, s_off
    mov ah, ATTR_OFF
    jmp put_text
.on:
    test al, 2
    jnz .limit
    mov si, s_zone
    test al, 16
    jnz .zone
    mov si, s_no_laws
    test al, 1
    jz .say
    mov si, s_open
.say:
    mov ah, ATTR_LABEL
    jmp put_text
.zone:
    mov ah, ATTR_LABEL
    call put_text
    jmp .whose
.limit:
    mov si, s_limit
    mov ah, ATTR_LABEL
    call put_text
    mov dx, RADIO + 12
    in ax, dx
    mov si, ax
    call fetch
    mov ax, [entry + 8]             ; 1/8 m/s
    mov cl, 3
    shr ax, cl
    call put_uint
    mov si, s_ms
    mov ah, ATTR_LABEL
    call put_text
.whose:
    mov dx, RADIO + 12
    in ax, dx
    mov si, ax
    call fetch
    mov ah, ATTR_STATION
    jmp put_name

; 16 bytes at table_seg:SI into entry; SI past them
fetch:
    push es
    push ds
    push di
    push cx
    push cs
    pop es
    mov di, entry
    mov ds, [table_seg]
    mov cx, 8
    rep movsw
    pop cx
    pop di
    pop ds
    pop es
    ret

; The callsign in entry (8 characters) with attribute AH, at ES:DI
put_name:
    mov si, entry
    mov cx, 8
.char:
    lodsb
    stosw
    loop .char
    ret

; AX a range in 10 m, at ES:DI: kilometres with a tenth, right-aligned in
; 6 columns
put_km:
    push bx
    xor dx, dx
    mov cx, 10
    div cx                          ; tenths of a kilometre
    add di, 10
    xor bl, bl
.digit:
    xor dx, dx
    div cx
    add dl, '0'
    mov dh, ATTR_VALUE
    mov [es:di], dx
    sub di, 2
    inc bl
    cmp bl, 1
    jne .more
    mov word [es:di], (ATTR_VALUE << 8) | '.'
    sub di, 2
    jmp .digit                      ; a digit before the point, always
.more:
    test ax, ax
    jnz .digit
    pop bx
    ret

; AX unsigned at ES:DI, left-aligned; DI past it
put_uint:
    mov cx, 10
    xor bx, bx
.divide:
    xor dx, dx
    div cx
    push dx
    inc bx
    test ax, ax
    jnz .divide
.emit:
    pop ax
    add al, '0'
    mov ah, ATTR_VALUE
    stosw
    dec bx
    jnz .emit
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

s_station   db 'STATION', 0
s_ship      db 'SHIP', 0
s_laws      db 'LAWS', 0
s_dock      db 'DOCK', 0
s_hangar    db 'HANGAR ', 0
s_limit     db 'SPEED LIMIT ', 0
s_ms        db ' m/s  ', 0
s_zone      db 'PROTECTED ZONE  ', 0
s_open      db 'OPEN SPACE: NO LIMIT', 0
s_no_laws   db 'NO LAWS RECEIVED', 0
s_off       db 'RADIO OFF', 0
s_option0   db ' REQUEST DOCKING              ', 0
s_option1   db ' REQUEST LAWS                 ', 0
s_no_options db 'NO OPTIONS FOR SHIPS', 0
s_hint      db 'SELECT: SEND   BACK: LIST', 0

countdown   db 1
key_count   db 0
my_screen   db 0FFh
mode        db 0                    ; 0 the list, 1 the chosen contact's options
cursor      db 0
option      db 0
count       db 0
shown       db 0
status      db 0
screen_seg  dw 0
table_seg   dw 0
sel_name    times 8 db ' '          ; the contact under the cursor
sel_kind    db 0                    ; (right after its callsign)
entry       times 16 db 0
msg         times MSG_SIZE db 0

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
    mov dx, RADIO
    in al, dx
    cmp al, TYPE_RADIO
    je .radio
    mov dx, msg_no_radio
    jmp quit_message
.radio:
    mov dx, DISPLAYS
    in al, dx
    cmp al, TYPE_DISPLAYS
    jne .no_screen
    mov si, n_comms
    call find_screen
    jc .no_screen
    cmp byte [es:bx + 7], COLS
    jne .no_screen
    cmp byte [es:bx + 8], ROWS
    jae .screen
.no_screen:
    mov dx, msg_no_screen
    jmp quit_message
.screen:
    mov dx, DISPLAYS + 4            ; its place in the table: its keys say it
    in ax, dx
    mov cx, bx
    sub cx, ax
    mov al, cl
    mov cl, 4
    shr al, cl
    mov [my_screen], al
    mov dx, DISPLAYS + 8
    in al, dx
    mov [key_count], al
    mov byte [es:bx + 6], 0         ; text
    mov ax, [es:bx + 14]
    mov cl, 4
    shr ax, cl
    mov cx, es
    add ax, cx
    mov [screen_seg], ax
    mov dx, RADIO + 8
    in ax, dx
    mov [table_seg], ax
    call draw_labels

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
    push es                         ; the screen says so
    mov es, [es:screen_seg]
    mov di, (LAWS_ROW * COLS + 1) * 2
    mov si, s_removed
    mov ah, ATTR_OFF
    call put_text
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

n_comms     db 'COMMS '

draw_labels:
    push es
    mov es, [screen_seg]
    xor di, di
    mov ax, (ATTR_LABEL << 8) | ' '
    mov cx, COLS * ROWS
    rep stosw
    xor di, di
    mov ax, (ATTR_TITLE << 8) | ' '
    mov cx, COLS
    rep stosw
    mov di, 1 * 2
    mov si, s_title
    mov ah, ATTR_TITLE
    call put_text
    mov di, (1 * COLS + 1) * 2
    mov si, s_header
    mov ah, ATTR_LABEL
    call put_text
    pop es
    ret

quit_message:
    mov ah, 09h
    int 21h
    mov ax, 4C01h
    int 21h

s_title         db 'RADIO 1.1', 0
s_header        db 'CALLSIGN  KIND       KM', 0
s_removed       db 'COMMS REMOVED', 0
msg_installed   db 'Radio resident: contacts on the COMMS screen.', 13, 10, '$'
msg_already     db 'Radio program is already resident.', 13, 10, '$'
msg_not_running db 'Radio program is not resident, or a program that is not', 13, 10
                db "one of the ship's took IRQ 5 after it.", 13, 10, '$'
msg_removed     db 'Radio program removed.', 13, 10, '$'
msg_no_radio    db 'COMMS: the ship has no radio.', 13, 10, '$'
msg_no_screen   db 'COMMS: the ship has no COMMS screen (32 x 12).', 13, 10, '$'
