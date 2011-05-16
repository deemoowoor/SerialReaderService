@ECHO OFF

REM The following directory is for .NET 4.0
rem set DOTNETFX4=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319
set PATH=%PATH%;%DOTNETFX4%

echo Installing SerialSpeedConverter...
echo ---------------------------------------------------
cs create "SerialSpeedConverter" binPath="C:\services\SerialSpeedConverter\SerialSpeedConverter.exe" type= own start= auto
sc start "SerialSpeedConverter"
echo ---------------------------------------------------
echo Done.