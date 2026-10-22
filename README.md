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
- `disks\cos.img`: el diskette de la nave, con COS, `AUTOEXEC.BAT` y el software de la nave: `FLIGHT.COM` (vuelo) y `FIRECTL.COM` (radar y torreta), con sus fuentes `.ASM` para editarlos dentro del juego.

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
| Fijar el blanco adelante / el siguiente | T / G |
| Disparar | Clic izquierdo, en modo vuelo |
| Mirar alrededor | Mouse, en modo vista |
| Escribir en la computadora | En modo vista, clic en la pantalla; clic derecho para soltarla |
| Guardar / cargar la computadora | F5 / F9 |

También hay cabeceo y alabeo con las flechas, y un joystick o gamepad funciona con los mismos controles. Todo está en el Input Map de Godot.

## La nave y su computadora

Cada paso de física (60 por segundo), `ShipBody` escribe la palanca, la muestra de la unidad inercial y lo que ven los demás dispositivos en los registros del bus, pide la IRQ 5, corre la computadora 1/60 s y aplica lo que el software dejó escrito.

| Ranura | Puertos | Dispositivo |
|---|---|---|
| 0 | `100h` | Mandos del piloto (palanca, acelerador, botones) |
| 1 | `110h` | Unidad inercial (pide la IRQ 5 con cada muestra) |
| 2 | `120h` | Propulsores (watchdog de 100 ms: sin comandos, modo directo) |
| 3 | `130h` | Radar (tabla de contactos y seguimiento en memoria, en D000:1000) |
| 4 | `140h` | Torreta con gimbal y cañón cinético de 1.200 m/s |
| 5 | `150h` | Pantallas laterales: texto de 40×16 en memoria (D000:0000 y D000:0800) |

AUTOEXEC.BAT deja residentes dos programas que trabajan dentro de la interrupción, así la consola queda libre:

- **FLIGHT.COM** (5% de la CPU): flight assist; página en la pantalla izquierda.
- **FIRECTL.COM** (26% de la CPU): fija blancos con T/G, calcula el adelanto (CORDIC para ángulos y distancias), apunta la torreta y dispara mientras se mantiene el gatillo; radar y estado en la pantalla derecha.

`FLIGHT OFF` y `FIRECTL OFF` los sacan de memoria (primero el último instalado). Los registros están documentados al principio de [flight.asm](software/flight/flight.asm) y [firectl.asm](software/firectl/firectl.asm).

## Pruebas

```bat
Godot_v4.7.2-stable_mono_win64_console.exe --headless --path . res://tests/computer_test.tscn
Godot_v4.7.2-stable_mono_win64_console.exe --headless --fixed-fps 60 --path . res://tests/flight_test.tscn
Godot_v4.7.2-stable_mono_win64_console.exe --headless --fixed-fps 60 --path . res://tests/combat_test.tscn
```

- `computer_test`: arranca COS, escribe `DIR`, guarda y carga la computadora, y mide cuántas máquinas a 4,77 MHz corren en un núcleo.
- `flight_test`: la nave volada por FLIGHT.COM. Prueba velocidades de giro y de avance con flight assist, la deriva sin él, el botón FA, la consola libre mientras vuela, y el modo directo con `FLIGHT OFF`.
- `combat_test`: FIRECTL.COM contra drones. Fija con T, mide el error de puntería contra la intercepción exacta, cuenta impactos de una ráfaga a 2 km, destruye el drone, cambia de blanco con G y apaga el radar con `FIRECTL OFF`.

`tests/combat_demo.tscn` abre el juego con un script en los controles (fija y dispara solo), para mirarlo o grabarlo.

Todas terminan con código 0 si todo pasó.

## Estructura

| Carpeta | Contenido |
|---|---|
| `scripts/Native` | P/Invoke sobre `pc86.dll` y `xpace_native.dll` |
| `scripts/Computer` | `ShipComputer` (la PC de la nave), `ShipBus` (los registros del bus), el mapa de teclas |
| `scripts/Ship` | `ShipBody` (física, mandos, unidad inercial, propulsores, watchdog), radar, torreta, pantallas laterales, `ShipSpec`, `PilotControls` |
| `scripts/Cockpit` | Piloto (controles estilo Elite, vista), pantallas de cabina, indicador del mouse, cabina provisoria |
| `scripts/Space` | Estrellas, asteroides, polvo espacial, estación provisoria, drones, proyectiles |
| `software` | Software de la nave: `flight/flight.asm`, `firectl/firectl.asm`, `autoexec.bat` |
| `scenes` | `main.tscn`: la nave en el campo de asteroides |
| `tests` | Pruebas de la computadora, de vuelo y de combate; demo de combate |
| `native` | La placa del bus y el dibujo de texto en C, y el script que compila las DLL y el diskette |
