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

Compila `native\bin\pc86.dll` y arma el diskette `disks\cos.img` desde los checkouts de pc86 y sw86 (por defecto `..\vm86\pc86` y `..\vm86\sw86`; se pueden pasar otras rutas como argumentos). Ninguno de los dos va al repositorio: se rehacen con este script.

Después abrir el proyecto en Godot (versión .NET) y compilar.

## Controles (cabina provisoria)

| Acción | Tecla |
|---|---|
| Mirar alrededor | Clic para tomar el mouse, después mover el mouse |
| Escribir en la computadora | Clic en la pantalla; clic derecho para soltarla |
| Guardar / cargar la computadora | F5 / F9 |
| Soltar el mouse | Esc (cuando no se está escribiendo) |

## Pruebas

```bat
Godot_v4.7.2-stable_mono_win64_console.exe --headless --path . res://tests/computer_test.tscn
```

Arranca COS, escribe `DIR`, guarda y carga la computadora, y mide cuántas máquinas a 4,77 MHz corren en un núcleo. Termina con código 0 si todo pasó.

## Estructura

| Carpeta | Contenido |
|---|---|
| `scripts/Native` | P/Invoke sobre `pc86.dll` |
| `scripts/Computer` | `ShipComputer` (la PC de la nave) y el mapa de teclas |
| `scripts/Cockpit` | Pantallas de cabina, el piloto sentado, la cabina provisoria |
| `scripts/Space` | Fondo de estrellas |
| `scenes` | `main.tscn`: la cabina de prueba |
| `tests` | Prueba de la computadora y benchmark |
| `native` | Script que compila la DLL y el diskette |
