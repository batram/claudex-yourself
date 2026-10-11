Option Explicit
' A GUI-subsystem host creates PowerShell hidden from its first instruction.
' -WindowStyle Hidden alone runs too late to prevent the console flashing.
Dim shell, command
If WScript.Arguments.Count <> 4 Then WScript.Quit 2
Set shell = CreateObject("WScript.Shell")
command = """" & WScript.Arguments(0) & """ -NoProfile -NonInteractive -File """ & WScript.Arguments(1) & """ -InPackage -Action " & WScript.Arguments(2) & " -ResultPath """ & WScript.Arguments(3) & """"
WScript.Quit shell.Run(command, 0, True)
