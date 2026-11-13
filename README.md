# LinkedIn o Interpol - OpenCV C# Edition

Juego interactivo desarrollado en **C# (.NET 10)** y **OpenCV (OpenCvSharp4)** con visión artificial en tiempo real.

## 🚀 Características
1. **Detección facial y recorte inteligente de retratos** con Haar Cascades de OpenCV.
2. **Control manos libres**: Juega inclinando tu rostro hacia la izquierda (**LinkedIn**) o hacia la derecha (**Interpol**).
3. **Escáner biométrico**: Analiza tu sonrisa, luminosidad y rostro para catalogarte como "Ejecutivo de LinkedIn" o "Criminal de Interpol" con cargos o puestos humorísticos generados, y te añade inmediatamente al banco de retratos del juego.
4. **Filtros en tiempo real**: Blanco y negro, desenfoque gaussiano para dificultar la adivinanza.

## 📁 Estructura del Proyecto
- `Program.cs`: Backend Minimal API con servicios `GameRepository` y `OpenCvService`.
- `wwwroot/index.html`: Frontend moderno con acceso a la cámara web (`getUserMedia`) y panel interactivo.
- `Assets/`: Directorio donde residen los retratos (con nombres `interpol_*` o `linkedin_*`) y donde se guardan tus capturas biométricas.
- `haarcascade_frontalface_default.xml`: Modelo Haar Cascade de detección frontal de rostros.
- `haarcascade_smile.xml`: Modelo Haar Cascade de detección de sonrisas.
- `Ejecutar_Juego.bat`: Script de inicio rápido.

## ⚡ Cómo ejecutarlo

### Opción 1: Acceso directo
Haz doble clic en:
```
Ejecutar_Juego.bat
```

### Opción 2: Terminal
```bash
cd LinkedInOrInterpolClone
dotnet run
```
Abre en tu navegador:
`http://localhost:5117`
