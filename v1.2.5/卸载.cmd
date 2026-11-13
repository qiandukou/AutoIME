@echo off
setlocal
taskkill /f /im AutoIME.exe >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v AutoIME /f >nul 2>&1
rmdir /s /q "%LOCALAPPDATA%\AutoIME" 2>nul
echo ÒÑÍ£Ö¹²¢É¾³ý AutoIME¡£
pause