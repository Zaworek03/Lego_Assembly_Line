@echo off
rem ============================================================
rem  Linia Montazowa - skrot do uruchomienia calego systemu.
rem  Podwojne klikniecie odpala Uruchom_Linie.ps1.
rem
rem  ExecutionPolicy Bypass dotyczy TYLKO tego jednego wywolania -
rem  nie zmienia zadnych ustawien systemu.
rem
rem  NIE uruchamiaj przez "Uruchom jako administrator": LocalDB
rem  podniesiona z takimi uprawnieniami jest niedostepna dla
rem  zwyklych procesow i strona nie polaczy sie z baza.
rem ============================================================
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uruchom_Linie.ps1" %*
if errorlevel 1 pause
