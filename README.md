# Xpace

Juego espacial (comercio, combate, facciones) donde los sistemas de las naves corren en computadoras 8086 programables. Nombre en clave.

- Motor: Godot 4.7 (.NET, C#).
- Diseño: [docs/GDD.md](docs/GDD.md).
- La computadora de la nave es la placa pc86 (CPU vm86, BIOS, VGA, teclado, timer, disquetera) de `D:\Juan\vm86\pc86`, con COS de `D:\Juan\vm86\sw86`.

## Preparar

Requiere Visual Studio 2022 (MSVC y su CMake) y el SDK de .NET 8 o posterior.

```bat
native\build.bat
```

Compila desde los checkouts de pc86 y sw86 (por defecto `..\vm86\pc86` y `..\vm86\sw86`; se pueden pasar otras rutas como argumentos):

- `native\bin\pc86.dll`: la máquina.
- `native\bin\xpace_native.dll`: la placa del bus de la nave ([native/xbus.c](native/xbus.c)).
- `disks\cos.img`: el diskette de la nave, con COS, `AUTOEXEC.BAT` y el software de vuelo (`FLIGHT.COM`, y su fuente `FLIGHT.ASM` para editarlo dentro del juego).

Nada de eso va al repositorio: se rehace con este script. Después abrir el proyecto en Godot (versión .NET) y compilar.

## Controles

Como en Elite Dangerous con teclado y mouse. El botón del medio del mouse cambia entre **vuelo** y **vista**.

| Acción | Control |
|---|---|
| Tomar el mouse | Clic (Esc lo suelta, si no se está escribiendo) |
| Cabeceo y alabeo | Mouse, en modo vuelo (joystick virtual: el anillo del centro muestra la palanca) |
| Acelerador | W / S (queda donde se deja; por debajo de cero es reversa), X a cero |
| Guiñada | A / D |
| Empuje lateral / vertical | Q / E, R / F |
| Flight assist | Z |
| Mirar alrededor | Mouse, en modo vista |
| Escribir en la computadora | En modo vista, clic en la pantalla; clic derecho para soltarla |
| Guardar / cargar la computadora | F5 / F9 |

También hay cabeceo y alabeo con las flechas, y un joystick o gamepad funciona con los mismos controles. Todo está en el Input Map de Godot.

## La nave y su computadora

Cada paso de física (60 por segundo), `ShipBody` escribe la palanca y la muestra de la unidad inercial en los registros del bus, pide la IRQ 5, corre la computadora 1/60 s y aplica lo que FLIGHT.COM dejó en los registros de los propulsores. Si el programa deja de escribir por 100 ms, el controlador de propulsores pasa a **modo directo**: la palanca va derecho a los propulsores. Los registros están documentados al principio de [software/flight/flight.asm](software/flight/flight.asm).

## Pruebas

```bat
Godot_v4.7.2-stable_mono_win64_console.exe --headless --path . res://tests/computer_test.tscn
Godot_v4.7.2-stable_mono_win64_console.exe --headless --fixed-fps 60 --path . res://tests/flight_test.tscn
```

- `computer_test`: arranca COS, escribe `DIR`, guarda y carga la computadora, y mide cuántas máquinas a 4,77 MHz corren en un núcleo.
- `flight_test`: la nave volada por FLIGHT.COM. Prueba velocidades de giro y de avance con flight assist, la deriva sin él, el botón FA, y el modo directo cuando el programa termina.

Las dos terminan con código 0 si todo pasó.

## Estructura

| Carpeta | Contenido |
|---|---|
| `scripts/Native` | P/Invoke sobre `pc86.dll` y `xpace_native.dll` |
| `scripts/Computer` | `ShipComputer` (la PC de la nave), `ShipBus` (los registros del bus), el mapa de teclas |
| `scripts/Ship` | `ShipBody` (física, dispositivos del bus, watchdog), `ShipSpec`, `PilotControls` |
| `scripts/Cockpit` | Piloto (controles estilo Elite, vista), pantallas de cabina, indicador del mouse, cabina provisoria |
| `scripts/Space` | Estrellas, asteroides, polvo espacial, estación provisoria |
| `software` | Software de la nave: `flight/flight.asm`, `autoexec.bat` |
| `scenes` | `main.tscn`: la nave en el campo de asteroides |
| `tests` | Prueba de la computadora, benchmark y prueba de vuelo |
| `native` | La placa del bus en C, y el script que compila las DLL y el diskette |
