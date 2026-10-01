# Rotar Norte — rotación segura del Norte de Proyecto en Revit

Add-in para Autodesk Revit (2022 a 2027) que gira el Norte de Proyecto **sin que desaparezcan
elementos, sin que se queden cosas sin girar y sin que las vistas pierdan su referencia**.

## El problema

La herramienta nativa *Gestionar > Posición > Rotar Norte de Proyecto* gira el modelo, pero en
proyectos reales suele dejar daños:

| Síntoma | Causa |
|---|---|
| Vínculos de Revit, DWG, rejillas o planos de referencia no giran | Están **anclados** y Revit los omite sin avisar |
| Elementos "desaparecen" de las vistas | La **región de recorte** de cada planta se queda donde estaba y el edificio se sale de ella |
| Vistas 3D quedan vacías o cortadas | La **caja de sección** no gira con el modelo |
| Secciones y alzados apuntan a otro sitio | Sus marcas no giran, o giran sin las anotaciones |
| Cotas y etiquetas se borran | Se giran las anotaciones en un paso distinto que sus referencias |
| Habitaciones quedan "no cerradas" | Su punto de ubicación no se mueve con los muros |
| Las coordenadas compartidas cambian | No se corrige el ángulo a Norte Verdadero al girar |
| Un elemento se gira dos veces o "explota" | Se gira a la vez un anfitrión (muro, suelo, escalera) y sus dependientes (puertas, barridos, tramos) |

## Qué hace el add-in

El botón **Rotar Norte de Proyecto** ejecuta, dentro de una única operación de deshacer:

1. **Analiza** el modelo y decide qué se gira explícitamente y qué se mueve solo con su anfitrión
   (puertas, ventanas, paneles de muro cortina, tramos de escalera, barridos, armaduras, etc.).
2. **Desancla temporalmente** todo lo anclado (vínculos, DWG, rejillas...) y lo vuelve a anclar al final.
3. Gira **en una sola llamada** el modelo, las anotaciones de las plantas, las marcas de sección,
   las llamadas de detalle y las marcas de alzado, igual que si se seleccionara todo y se girara a
   mano: cotas, etiquetas y referencias se mantienen coherentes.
4. Si Revit rechaza la operación, **aísla por bisección los elementos culpables** (en un grupo de
   transacciones que se deshace), los excluye, repite y los lista en el informe.
5. Recrea ya giradas las familias que Revit no permite girar (basadas en plano vertical) y
   traslada **habitaciones, espacios y áreas** por su punto de ubicación.
6. Ajusta las **vistas**: regiones de recorte de planta, cajas de sección y cámaras 3D, y
   recentra las ventanas gráficas en los planos para que la maquetación no se mueva.
7. Corrige el **ángulo a Norte Verdadero** para que las coordenadas compartidas de todo el
   edificio queden exactamente igual (se verifica numéricamente con un punto de prueba).
8. Genera un **informe** con todo lo girado, lo omitido y lo que no se pudo girar.

Todo va dentro de un `TransactionGroup`: un solo **Ctrl+Z** lo revierte por completo. Ante
cualquier error (con la opción recomendada) se cancela todo y el modelo queda intacto.

### Modo simulación

Marque **"Solo simular"**: el add-in hace todo el trabajo, genera el informe y lo deshace. Es la
forma recomendada de empezar en cualquier modelo grande.

### Los tres botones de la pestaña *Rotar Norte*

| Botón | Para qué sirve |
|---|---|
| **Rotar Norte de Proyecto** | La rotación segura descrita arriba, con diálogo de opciones e informe. |
| **Rotar Norte Verdadero** | Cambia solo el ángulo a Norte Verdadero. No mueve nada. Si lo que quiere es corregir la orientación real del edificio (sol, coordenadas), **esta es la opción correcta** y no hace falta girar el Norte de Proyecto. |
| **Enderezar vista** | Quita la rotación de la región de recorte de la planta activa (o de las seleccionadas en el Navegador) conservando el contenido. Útil tras una rotación en modo "conservar aspecto". |

## Opciones del diálogo

- **Ángulo**: grados, positivo = antihorario visto en planta. *Medir con 2 puntos* calcula el giro
  necesario para que la dirección marcada apunte al Norte de Proyecto; *Alinear con eje
  horizontal* hace lo mismo con el eje Este-Oeste. Se elige siempre el giro más corto.
- **Centro de giro**: Punto base del proyecto (recomendado), origen interno, punto de
  levantamiento o un punto elegido en pantalla.
- **Vistas de planta orientadas a Norte de Proyecto**:
  - *Conservar el aspecto actual* (recomendado): la región de recorte gira con el modelo, así que
    cada vista y cada plano se ven exactamente igual que antes. Después puede enderezar las vistas
    que quiera con *Enderezar vista*.
  - *Mostrar la nueva orientación*: la vista no gira y el recorte se reajusta para abarcar lo mismo;
    el modelo y sus anotaciones aparecen girados.
  - Las vistas orientadas a **Norte Verdadero** no cambian de aspecto en ningún caso, porque la
    orientación real del edificio se conserva.
  - Las vistas controladas por una **caja de referencia** siguen a la caja, que gira con el modelo.
- **Mantener el Norte Verdadero y las coordenadas compartidas**: recomendado. Se aplica a todos
  los emplazamientos del proyecto.
- **Desanclar temporalmente**: si se desactiva, lo anclado no se gira y se lista como omitido.
- **Girar anotaciones / secciones y alzados / vistas 3D**: normalmente todo activado.
- **Recrear giradas las familias que Revit no permite girar**: las familias basadas en cara o en
  plano de trabajo vertical o inclinado (por ejemplo conexiones colocadas en la cara de una viga y
  sin anfitrión) producen el error *"Can't rotate element into this position"* al girarlas en
  planta, también a mano. El add-in las copia con la transformación de giro, como "Pegar
  alineado", y borra la original. Conservan sus parámetros y su anclaje, pero **cambian de Id**
  (se pierden sus etiquetas si las tenían). Si se desactiva, se dejan en su sitio y se listan.
- **Si Revit informa de errores**: *Cancelar todo* (recomendado) o *aplicar la resolución
  automática de Revit* (puede borrar elementos, como haría Revit en el diálogo de errores).

## Instalación

### Requisitos

- Revit 2022, 2023, 2024, 2025, 2026 o 2027 (Windows).
- Para compilar: [SDK de .NET 10](https://dotnet.microsoft.com/download) para Revit 2027
  (SDK de .NET 8 para versiones anteriores) o Visual Studio 2022.
  No hace falta tener Revit instalado para compilar: el API se descarga de NuGet.

### Opción A: script (recomendada)

```powershell
git clone https://github.com/Andy-rba30/Rotar-norte.git
cd Rotar-norte
.\scripts\install.ps1 -RevitVersion 2027
```

Compila y copia `RotarNorte.dll` y `RotarNorte.addin` a
`%APPDATA%\Autodesk\Revit\Addins\2027\`. Reinicie Revit y aparecerá la pestaña **Rotar Norte**.
Para desinstalar: `.\scripts\install.ps1 -RevitVersion 2027 -Uninstall`.

### Opción B: manual

```powershell
dotnet build src\RotarNorte\RotarNorte.csproj -c R2027
```

Copie `src\RotarNorte\bin\R2027\RotarNorte.dll` a
`%APPDATA%\Autodesk\Revit\Addins\2027\RotarNorte\` y `src\RotarNorte\RotarNorte.addin` a
`%APPDATA%\Autodesk\Revit\Addins\2027\`. Cambie `R2027` y `2027` por su versión.

### Opción C: GitHub Actions

Cada push compila el add-in en Windows para las seis versiones y deja un artefacto
`RotarNorte_Revit20XX` descargable en la pestaña *Actions* del repositorio, con la estructura de
carpetas lista para copiar en `%APPDATA%\Autodesk\Revit\Addins\20XX\`.

## Uso recomendado

1. Haga una **copia de seguridad** (o trabaje sobre una copia separada del central).
2. En modelos de **trabajo compartido**: sincronice, ceda todos los elementos y pida a los demás
   que sincronicen y cedan. El add-in solicita la propiedad de todo lo que va a girar; lo que
   pertenezca a otro usuario se omite y se lista en el informe.
3. Ejecute primero en modo **"Solo simular"** y revise el informe (se puede guardar como texto).
4. Ejecute la rotación real. Revise las vistas y, si eligió "conservar aspecto", enderece las
   vistas que quiera con *Enderezar vista*.
5. Si algo no le convence: **Ctrl+Z** deshace toda la operación de una vez.

## Limitaciones conocidas

- Las anotaciones de **secciones y alzados** no se giran aparte: se confía en que Revit las mueva
  con la marca, como hace al girarla en la interfaz. Revise sus secciones tras la rotación.
- Las regiones de recorte **divididas** se conservan solo con su primer contorno (se avisa en el
  informe).
- Las **cajas de referencia** giran con el modelo, pero su tamaño no puede reajustarse por API.
- Los elementos de **modelo analítico** se dejan a Revit; en Revit 2023 o posterior, si usa
  elementos analíticos independientes, revíselos.
- Vistas 3D **bloqueadas**: se gira la caja de sección pero no la cámara.
- La simulación en modelos de trabajo compartido toma prestados elementos del central; cédalos al
  sincronizar.

## Estructura del código

```
src/RotarNorte/
  App.cs                              Pestaña y botones de la cinta
  RotarNorte.addin                    Manifiesto del add-in
  Commands/
    RotarNorteProyectoCommand.cs      Comando principal (diálogo, medición de ángulo, informe)
    RotarNorteVerdaderoCommand.cs     Cambio del ángulo a Norte Verdadero
    EnderezarVistaCommand.cs          Quitar la rotación de recorte de una planta
  Core/
    NorthRotationEngine.cs            Motor: transacciones, rotación en bloque, bisección, coordenadas
    CandidateCollector.cs             Qué se gira y qué se omite (anfitrionados, grupos, paneles...)
    ViewRotator.cs                    Recortes de planta, cajas de sección, cámaras, ventanas gráficas
    FailureCollector.cs               Preprocesador de avisos/errores de Revit
    RotationOptions.cs / RotationReport.cs / Compat.cs
  UI/                                 Formularios WinForms (diálogo, progreso, informe)
scripts/install.ps1                   Compilar e instalar
scripts/build-all.ps1                 Compilar para todas las versiones y empaquetar en dist/
.github/workflows/build.yml           Compilación en GitHub Actions
```

Cada configuración de compilación (`R2022`…`R2027`) selecciona el paquete NuGet del API de Revit
correspondiente y el framework adecuado (`net48` hasta 2024, `net8.0-windows` en 2025 y 2026,
`net10.0-windows` desde 2027).
