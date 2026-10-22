# Xpace: documento maestro de diseño

Versión 0.2, 22 de octubre de 2026. Es un documento vivo: las secciones marcadas **[abierto]** todavía están por decidir.

Nombre en clave: **Xpace**. El nombre definitivo del juego y de la galaxia está pendiente (sección 13).

---

## 1. La idea en una frase

Un sandbox espacial de comercio, combate y facciones, al estilo de X4, Elite Dangerous, Avorion y No Man's Sky, donde **el cerebro de cada nave es una computadora 8086 real**. Navegación, puntería, radar, radios, misiles y torretas funcionan con software que corre en esa CPU. El jugador puede usar el que viene de fábrica, comprarlo o escribir el suyo.

## 2. Pilares

1. **La computadora es la nave.** Todo sistema digital pasa por una CPU emulada. No es una capa decorativa: si tu software de puntería es mejor, le acertás más al enemigo.
2. **Programar es opcional, nunca obligatorio.** Cada sistema trae software de fábrica que funciona bien. Programar sirve para superarlo, no para poder jugar.
3. **Combate con sensores creíbles.** Radar, firma infrarroja, alerta de radar (RWR), contramedidas y misiles guiados como en un avión de combate moderno, sumados a lo clásico de la ciencia ficción: láseres, railguns y escudos.
4. **Un universo con dueños.** Corporaciones, facciones y política que existen sin el jugador, con una economía que produce, transporta y consume.
5. **Sé lo que quieras.** Comerciante, minero, constructor de naves, diseñador de software, político o pirata.

## 3. Presentación y jugador

### 3.1 Cámara y cabina (decidido)

- **Siempre en primera persona, desde la cabina.** No hay vista externa de pilotaje.
- **Las pantallas de la computadora están en la cabina.** Las pantallas multifunción (MFD) muestran lo que dibuja la CPU de la nave: texto CGA, gráficos VGA, el radar o el editor. Lo que corre en el 8086 se ve físicamente adelante del piloto, lo que refuerza el pilar 1.
- **Naves chicas (cazas):** el jugador solo tiene acceso a la cabina.
- **Naves grandes (futuro):** el jugador puede levantarse del asiento y caminar por la nave.

### 3.2 El jugador tiene cuerpo

- Hay un personaje físico, aunque al principio casi siempre esté sentado en la cabina.
- Caminar por naves grandes y por estaciones queda para más adelante. La arquitectura tiene que permitirlo desde el principio: el personaje es una entidad separada de la nave, que "se sienta" en ella.

### 3.3 Arte

- Se usa el kit **Synty POLYGON Sci-Fi Space**. Trae naves y cabinas preparadas para primera persona, sin interiores, y alcanza para la etapa actual.
- El estilo low-poly de Synty define la dirección de arte. Los recursos propios (pantallas, HUD, estaciones nuevas) deben seguir ese estilo.
- Los interiores para caminar por las naves grandes van a necesitar otro kit o arte propio más adelante.

### 3.4 Muerte y seguro (decidido)

- **Sin muerte permanente.**
- Al ser destruido, el jugador reaparece en una estación. Recupera la nave pagando el **seguro**, un porcentaje de su valor, como en Elite Dangerous.
- Si no le alcanza el dinero, recibe una nave básica. **[abierto]** ¿Se pierde la carga? ¿Y el software instalado, o queda guardado en la cuenta del jugador?

## 4. Universo y desplazamiento

### 4.1 Estructura

- **Sector = un sistema solar.** Cada sector tiene una estrella, planetas, cinturones de asteroides, estaciones, puertas de salto y anomalías.
- **Mapa de sectores:** un grafo de cientos de sectores. Empieza hecho a mano en el centro poblado y pasa a ser generado proceduralmente hacia la frontera.
- **Escala:** dentro del sistema, distancias "jugables" y comprimidas (como en X4), no las reales (como en Elite). Así un viaje entre planetas dura minutos y no horas.
- **Planetas:** por ahora no se puede aterrizar (decidido). Son decorado y referencia de navegación. Lo que se visita son estaciones espaciales y estructuras parecidas: astilleros, puestos mineros, bases piratas, ruinas. Aterrizar en planetas queda para el futuro.

### 4.2 Moverse entre sectores: saltos (decidido)

Hay dos formas de saltar, y conviven:

| Forma | Cómo funciona | Por qué existe |
|---|---|---|
| **Puertas de salto** | Estructuras fijas que unen sectores vecinos. Son instantáneas y seguras, pero tienen dueño: cobran peaje, piden permiso y pueden cerrarse. | Crean rutas comerciales, cuellos de botella y política. Controlar una puerta es poder. |
| **Motor de salto** | Va en la nave. Necesita combustible y un tiempo de carga que crece con la masa. Las coordenadas las **calcula la computadora de navegación**. | Libertad, exploración y huidas. Lo usan contrabandistas y piratas. |

El cálculo del salto es un buen ejemplo de la CPU como mecánica: un programa de navegación mejor, o una CPU más rápida, calcula el salto antes o con menos error. Un salto mal calculado te deja lejos del destino, o en un lugar peligroso.

### 4.3 Moverse dentro del sistema: tiempo real

- **Vuelo normal:** newtoniano en 6 grados de libertad, para combate y maniobras.
- **Crucero:** un modo de alta velocidad para recorrer el sistema. Se corta cerca de masas grandes y puede ser interdictado.
- Las autopistas espaciales de X4 quedan descartadas. Las puertas y el crucero cubren ese rol.

## 5. La computadora de a bordo

### 5.1 Hardware

- **CPU:** el núcleo vm86 de pc86, un 8086 fiel con 1 MB de memoria, puertos de E/S e interrupciones. Corre a 4,77 MHz de referencia, unas 400.000 instrucciones por segundo.
- **Bus de expansión ("Xbus"):** cada módulo de la nave es un dispositivo con su rango de puertos: la ranura *n* ocupa los puertos `100h + n·16`. Desde M1 hay tres dispositivos:
  - los mandos del piloto, en `100h`;
  - la unidad inercial, en `110h`, que pide la IRQ 5 con cada muestra, 60 veces por segundo;
  - el controlador de propulsores, en `120h`, con un watchdog: si el software deja de escribir por 100 ms, pasa a modo directo.

  Los registros están documentados en `software/flight/flight.asm`. El software de vuelo de fábrica (FLIGHT.COM, que arranca desde AUTOEXEC.BAT) usa un 5% de la CPU de 4,77 MHz.

  M2 sumó tres dispositivos más:
  - el radar, en `130h`: cono de ±60° y 8 km, tabla de contactos y seguimiento escritos en memoria, en D000:1000;
  - la torreta con gimbal, en `140h`: servos de 90°/s y cañón cinético de 1.200 m/s;
  - las pantallas laterales, en `150h`: texto de 40×16 mapeado en memoria, como una CGA, en D000:0000 y D000:0800.

  FLIGHT y FIRECTL quedan **residentes** y trabajan dentro de la interrupción, así que la consola central queda libre. FIRECTL calcula el adelanto con CORDIC (ángulos y distancias sin raíz cuadrada) y usa un 26% de la CPU: el control de fuego pesa cinco veces más que el de vuelo. Es la mecánica del tiempo de CPU como recurso, ya funcionando.
- **Descubrimiento de hardware:** el registro 0 de cada ranura devuelve el ID del dispositivo, el fabricante y la versión, como hacía la instrucción HWQ en 0x10c. El software recorre el bus y averigua qué tiene instalado.
- **Interrupciones:** los dispositivos avisan sin que la CPU tenga que preguntar: un contacto nuevo en el radar, un misil que se acerca, un mensaje de radio, un daño.
- **Mejoras de hardware:**
  - CPUs más rápidas (8 MHz, 10 MHz, "Turbo");
  - más memoria;
  - **coprocesador matemático** (el 8087, como módulo aparte): hace barato el punto flotante;
  - **controladores dedicados**: CPUs extra, por ejemplo una por torreta, para descargar a la principal.

**El tiempo de CPU es un recurso, como la energía o el calor.** Un 8086 hace unas 6.600 instrucciones por cuadro a 60 Hz. Mantener tres torretas con cálculo de adelanto, más radar, más navegación, no entra en una sola CPU barata. Eso obliga a elegir entre comprar hardware y optimizar software.

### 5.2 Software

- **Sistema operativo:** COS, el DOS de CucsiCorp que ya existe en sw86. La nave arranca desde su "diskette" (en el juego, un cartucho o una ROM).
- **Lenguajes dentro del juego:** CBasic, para el que recién empieza, y ASM, para el que necesita velocidad. Los dos ya existen, junto con el editor EDIT.
- **Software de fábrica:** cada fabricante vende sus programas: control de vuelo, puntería, guerra electrónica, navegación y comercio automatizado.
- **El software es un producto:** tiene versiones, licencias, bugs y parches. Hay software pirata y un mercado de programas hechos por jugadores (en un primer momento, dentro del propio juego).

### 5.3 Ejemplos de lo que hace la CPU

| Sistema | Qué hace el software |
|---|---|
| **Control de vuelo (flight assist)** | Traduce la palanca en encendidos de propulsores. Amortigua la rotación y mantiene la velocidad. Se puede reemplazar o apagar. |
| **Torretas con gimbal** | Leen el blanco que fijó el radar, calculan el punto de intercepción con adelanto y mueven los servos. |
| **Misiles guiados por radar** | El misil tiene un microcontrolador propio. Puede recibir actualizaciones del radar de la nave madre por enlace de datos (guiado semiactivo) o encender su propio radar al final (activo, "fire and forget"). |
| **Misiles infrarrojos** | El buscador entrega un ángulo hacia la fuente de calor y el software aplica navegación proporcional. Las bengalas y la firma térmica de la nave entran en el cálculo. |
| **Radar** | Modos de búsqueda y seguimiento, y seguimiento mientras se busca (TWS). Fija blancos y prioriza amenazas. |
| **Guerra electrónica** | Interferencia, señuelos y análisis de la alerta de radar (RWR). |
| **Comunicaciones** | Radio y módem para hablar con estaciones, flotas y otras naves. Sirven para comerciar, coordinarse y también para **hackear**. |
| **Automatización** | Rutas de comercio y minería, y órdenes a naves de una flota propia. |

## 6. Combate

### 6.1 Sensores y firmas

- Cada nave tiene una **firma de radar** (según el tamaño y el casco) y una **firma infrarroja** (según el calor de motores y armas).
- **Sigilo:** apagar sistemas, enfriar la nave y volar con el radar apagado (modo pasivo).
- **Alerta de radar (RWR):** te avisa cuando te están buscando, siguiendo o tirando un misil.

### 6.2 Armas

| Tipo | Rol |
|---|---|
| Misiles IR | Corto alcance, "dispara y olvídate". Se los engaña con bengalas. |
| Misiles por radar | Medio y largo alcance. Se los engaña con chaff y interferencia. |
| Láseres | Impacto instantáneo. Gastan mucha energía y generan calor. |
| Railguns | Proyectil muy rápido con mucho daño. Necesitan adelanto (la CPU) y una carga previa. |
| Cañones balísticos | Baratos, gastan munición y les va bien contra cascos. |
| Torpedos | Contra naves capitales. Son lentos y se pueden interceptar. |

### 6.3 Defensas

Escudos, blindaje, sistemas de defensa de punto (point defense) y contramedidas: bengalas, chaff, interferencia y señuelos.

### 6.4 Energía y calor

- El reactor genera energía y se reparte entre armas, escudos, motores y sistemas, con gestión en vivo al estilo de los "pips" de Elite.
- Disparar, acelerar y saltar genera **calor**. El calor sube la firma infrarroja y, si se excede, daña módulos.
- Apagar y encender sistemas es parte de la táctica, y también se puede hacer desde software.

## 7. Equipamiento de naves (fittings)

- **Tamaños de ranura:** S, M, L y XL (capitales).
- **Tipos de ranura:** armas, utilitarios, internos y ranuras del bus de la computadora.
- **Restricciones:** energía, calor, masa, ranuras del bus y **tiempo de CPU**.
- **Cascos de fábrica (decidido):** cada nave es un modelo de un fabricante. No hay construcción libre por bloques al estilo de Avorion.
- **Partes modulares:** algunas partes del casco se pueden cambiar, como alas, góndolas de motor y módulos de carga, si el kit de Synty lo permite. Cambian el aspecto y también los números (masa, firma, ranuras). **[abierto]** Falta revisar qué piezas intercambiables trae el kit.

## 8. Economía, corporaciones y política

### 8.1 Economía

- Las estaciones producen a partir de materias primas, en cadenas de producción como las de X4: mineral → metal → componentes → módulos → naves.
- La economía está simulada: precios por oferta y demanda, y naves comerciantes que existen de verdad y pueden ser atacadas.

### 8.2 Corporaciones

- Muchas compañías venden cascos, módulos, CPUs y software.
- Cada una tiene su estilo y su ID de fabricante en el bus.
- **CucsiCorp** encaja como el gigante del hardware y los sistemas operativos.
- Compiten entre sí: lanzan productos, tienen escándalos y sacan parches.

### 8.3 Facciones y política

- Hay gobiernos, corporaciones, gremios y piratas, con relaciones entre sí, territorio y guerras.
- **Reputación** con cada facción.
- **Punto de partida (decidido): la política de Elite Dangerous.**
  - Las facciones tienen influencia en cada sector, que sube o baja según las acciones de los jugadores: misiones, comercio, recompensas y combate.
  - Las facciones tienen estados: auge, guerra, elecciones, crisis, etc.
  - El control de estaciones y puertas cambia cuando cambia la influencia.
- **Expansiones posibles [abierto]**, a decidir una vez que la base funcione:
  - controlar estaciones y puertas directamente;
  - fijar impuestos y peajes;
  - armar alianzas;
  - fundar una facción propia.

## 9. Roles del jugador

| Rol | Bucle principal |
|---|---|
| Comerciante | Comprar barato, transportar y vender. Escala a flotas automatizadas con software propio. |
| Minero | Prospectar, extraer y refinar. |
| Constructor | Construir estaciones y fábricas. Equipar y personalizar naves de fábrica (partes modulares, módulos y software) y venderlas. |
| Programador o corporación de software | Escribir programas y venderlos o licenciarlos. |
| Mercenario o cazarrecompensas | Contratos de combate. |
| Pirata | Interdictar, robar y hackear. Termina en un imperio pirata. |
| Político | Influencia, territorio y facción propia. |

## 10. Tecnología

- **Motor:** Godot 4.7 (.NET, C#).
- **CPU:** vm86 compilado como DLL nativa y llamado desde C# con P/Invoke. Su API en C (`vm86.h`) ya está pensada para eso:
  - sin hilos y sin reloj propio: el motor decide cuántas instrucciones corre por cuadro;
  - el estado completo se guarda y se restaura como un bloque de bytes, lo que sirve para guardar partidas.
- **Dispositivos:** cada módulo de la nave es una clase C# que implementa `in8`/`out8` y pide interrupciones.
- **Nivel de detalle de la simulación** (indispensable):
  - **Cerca del jugador:** las CPUs corren de verdad, instrucción por instrucción.
  - **Lejos:** las naves de la IA usan un modelo abstracto ("el software de fábrica nivel 3 acierta X%"), sin emular.
  - **Misiles:** emulados solo si los disparó el jugador o si van hacia él.
- **Presupuesto de CPU de la PC real:** medido en M0, la placa pc86 completa ejecuta unos 107 millones de instrucciones por segundo en un núcleo (una PC de oficina de 2026, compilación Release de MSVC). Eso equivale a unas **260 máquinas a 4,77 MHz por núcleo**. Alcanza de sobra para la nave del jugador, sus torretas y los misiles cercanos. Hoy cada instancia reserva 1 MB; para misiles conviene una variante de vm86 con memoria chica, de 64 KB o menos.
- **Escala espacial:** usar un origen flotante y evaluar la compilación de Godot con doble precisión.
- **Herramientas:** asm86 en la PC para el software de fábrica; COS, ASM y CBasic dentro del juego para el jugador.

## 11. Opinión y recomendaciones

**Lo más fuerte.** La idea tiene algo que no tiene ninguno de sus referentes: en X4 y en Elite la nave es una planilla de números, y acá la nave es una máquina que se entiende y se modifica. Además, buena parte del hardware y el software ya existe y funciona: el 8086, COS, el ensamblador, CBasic y EDIT. Eso es una ventaja enorme frente a 0x10c, que tuvo que inventar todo eso.

**El riesgo principal es el alcance.** Lo que se describe acá son cuatro juegos: un simulador de combate, un X4, un Avorion y un 0x10c. 0x10c se canceló justamente por intentar abarcar demasiado. Mi recomendación es construir en capas, con cada capa jugable por sí sola:

1. **Una nave, un sector, combate.** Vuelo newtoniano, radar, un misil y una torreta, todo controlado por software de fábrica que corre en vm86. Si esto no es divertido, nada de lo demás lo salva.
2. **Programar la nave.** Una terminal dentro del juego con COS, la posibilidad de cambiar un programa y ver la diferencia en combate.
3. **Varios sectores, saltos y comercio simple.**
4. **Economía simulada, facciones y corporaciones.**
5. **Construcción, política y flotas.**

**Sobre la programación.**
- El 95% de los jugadores no va a programar. El juego tiene que ser bueno sin programar, y programar tiene que dar ventajas visibles pero no obligatorias.
- El software de fábrica debería tener **parámetros configurables**, por ejemplo con archivos de configuración o un menú. Así el escalón entre "usar" y "programar" es más suave.

**Sobre la cabina en primera persona.** Es la mejor decisión para este juego, porque las pantallas de la cabina son el lugar natural donde mostrar la computadora. Tiene dos consecuencias técnicas:
- Cada pantalla de la cabina es una textura que se actualiza con el video de la vm86. Eso cuesta poco: un modo de texto de 80×25 o una pantalla de 320×200.
- Las cabinas de Synty probablemente no traen superficies pensadas como pantallas. Seguramente haya que agregar o adaptar las mallas de las MFD en cada cabina.

**Sobre los cascos de fábrica.** Es la opción correcta para el alcance: cascos fijos con partes modulares dan variedad sin el costo de un sistema de construcción. Además le dan sentido a las corporaciones, porque cada modelo tiene un fabricante.

**Sobre el seguro.** Encaja bien con la economía, pero conviene decidir pronto qué pasa con el software. Si un programa escrito por el jugador se pierde al morir, programar se vuelve frustrante. Recomiendo que el software sea del jugador y no de la nave: queda guardado y se reinstala.

**Sobre los saltos.** Coincido con ir por saltos: dan identidad, como en Star Wars y Star Trek, y evitan modelar autopistas entre sistemas. Combinar puertas con dueño y motores de salto libres da política y libertad a la vez.

**Sobre el realismo.** El combate "de avión de combate" (radar, firmas, contramedidas) es más interesante que el realismo físico total. Recomiendo vuelo newtoniano con un flight assist de software (que la CPU puede mejorar), distancias comprimidas, y velocidades y alcances pensados para que los encuentros duren segundos o minutos, no horas.

**Sobre el multijugador.** Conviene empezar solo con un jugador. Emular CPUs en red, con su determinismo y su sincronización, es un proyecto en sí mismo. Si se hace más adelante, la arquitectura de vm86 (sin reloj propio y con estado serializable) lo permite.

**Sobre hackear otras naves.** Es muy atractivo porque los módems y las radios lo hacen natural, pero puede romper el equilibrio. Habría que encararlo como protocolos con vulnerabilidades concretas, que las corporaciones parchean, y no como un botón de "hackear".

## 12. Hoja de ruta sugerida

| Hito | Contenido | Señal de éxito |
|---|---|---|
| M0 ✔ | vm86 como DLL dentro de Godot, una consola de texto en pantalla, COS arrancando | `A:\>` en una pantalla de la cabina |
| M1 ✔ | Vuelo 6DOF con flight assist corriendo en la CPU | La nave se maneja bien y el flight assist es un programa reemplazable |
| M2 ✔ | Radar, fijado de blanco, una torreta con gimbal controlada por software | La torreta sigue al blanco gracias al programa |
| M3 | Misiles IR y por radar, contramedidas, calor y energía | Un duelo contra la IA que dura 1–3 minutos y es divertido |
| M4 | Terminal en la cabina: editar y recompilar software dentro del juego | Cambiar el programa de la torreta mejora la puntería |
| M5 | Varios sectores, saltos, estaciones, comercio | Un viaje de comercio de punta a punta |
| M6+ | Economía, facciones, corporaciones, construcción, política | — |

## 13. Decisiones y preguntas abiertas

### Decidido

| Tema | Decisión |
|---|---|
| Cámara | Siempre en primera persona, desde la cabina. |
| Cuerpo | El jugador tiene cuerpo. En cazas solo hay cabina; caminar por naves grandes y estaciones queda para el futuro. |
| Arte | Synty POLYGON Sci-Fi Space: naves y cabinas en primera persona, sin interiores por ahora. |
| Naves | Cascos de fábrica, con partes modulares si el kit lo permite. |
| Política | Base al estilo de Elite Dangerous (influencia y estados de facción); se expande después. |
| Planetas | No se aterriza por ahora; solo estaciones y estructuras. Aterrizar queda para el futuro. |
| Muerte | Sin muerte permanente; seguro al estilo de Elite. |
| Viaje entre sectores | Saltos: puertas con dueño y motor de salto propio. |
| Nombre | Nombre en clave Xpace. |

### Pendiente

- **Nombre definitivo del juego y de la galaxia.** Hay que decidirlo pronto.
- Qué partes modulares trae el kit de Synty y cómo se reflejan en los números.
- Seguro: ¿qué se pierde al morir (carga, software)?
- Hasta dónde llega la política más allá de la base de Elite.
