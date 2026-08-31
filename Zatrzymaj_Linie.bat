@echo off
rem ============================================================
rem  Linia Montazowa - zatrzymanie strony i middleware.
rem  Oba programy chodza w tle bez okien, wiec zamyka sie je stad.
rem ============================================================
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Zatrzymaj_Linie.ps1" %*
