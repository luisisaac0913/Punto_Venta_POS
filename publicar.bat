@echo off
setlocal enabledelayedexpansion

if "%~1"=="" (
    echo [ERROR] Debes especificar la version a empaquetar.
    echo Uso: publicar.bat ^<version^>
    echo Ejemplo: publicar.bat 1.0.1
    exit /b 1
)

set VERSION=%~1

echo ======================================================
echo [1/2] Compilando en modo Release (win-x64 self-contained)...
echo ======================================================
dotnet publish "Punto de Venta\Punto de Venta.csproj" -c Release -r win-x64 --self-contained -o .\publish
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Fallo dotnet publish.
    exit /b %ERRORLEVEL%
)

echo.
echo ======================================================
echo [2/2] Generando instalador y deltas con Velopack (%VERSION%)...
echo ======================================================
vpk pack --packId "PuntoDeVenta" --packTitle "Punto de Venta" --packVersion "%VERSION%" --runtime win-x64 --packDir ".\publish" --mainExe "Punto de Venta.exe" --icon "Punto de Venta\CHC_icono.ico" --outputDir ".\Releases"
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Fallo vpk pack. Asegurate de tener vpk instalado con: dotnet tool install -g vpk
    exit /b %ERRORLEVEL%
)

echo.
echo ======================================================
echo PROCESO COMPLETADO EXITOSAMENTE
echo Version: %VERSION%
echo Archivos listos en: .\Releases
echo ======================================================
echo Siguiente paso: Sube los archivos de .\Releases a GitHub Releases con el tag v%VERSION%.

