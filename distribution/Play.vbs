' Launches the game behind a splash window, with no console flashing on screen.
' The shortcut created by the installer points here.
Option Explicit

Dim shell, fso, here, splash, gameDir, command
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

here = fso.GetParentFolderName(WScript.ScriptFullName)
splash = fso.BuildPath(here, "Splash.ps1")

' The installer writes the game folder next to this script.
If fso.FileExists(fso.BuildPath(here, "gamepath.txt")) Then
    Dim stream
    Set stream = fso.OpenTextFile(fso.BuildPath(here, "gamepath.txt"), 1)
    gameDir = StripMark(stream.ReadLine())
    stream.Close
Else
    gameDir = here
End If

If Not fso.FileExists(fso.BuildPath(gameDir, "Megabonk.exe")) Then
    MsgBox "Could not find Megabonk.exe in:" & vbCrLf & gameDir & vbCrLf & vbCrLf & _
           "Run Install.cmd again to repair the installation.", 16, "JOVANISMOF"
    WScript.Quit 1
End If

If fso.FileExists(splash) Then
    command = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File """ & _
              splash & """ -GameDirectory """ & gameDir & """"
    shell.Run command, 0, False
Else
    ' No splash available; start the game rather than leaving the player with nothing.
    shell.CurrentDirectory = gameDir
    shell.Run """" & fso.BuildPath(gameDir, "Megabonk.exe") & """", 1, False
End If

' A byte-order mark at the start of the path file would otherwise be read as part of the
' path itself, which is how an installed launcher can end up looking in a folder that
' cannot exist. Strip it whether it arrives as three ANSI characters or one wide one.
Function StripMark(value)
    Dim result, code
    result = value
    If IsNull(result) Then result = ""

    If Len(result) >= 3 Then
        If Asc(Mid(result, 1, 1)) = 239 And Asc(Mid(result, 2, 1)) = 187 And Asc(Mid(result, 3, 1)) = 191 Then
            result = Mid(result, 4)
        End If
    End If

    Do While Len(result) > 0
        code = AscW(Left(result, 1))
        If code = 65279 Or code = -257 Then
            result = Mid(result, 2)
        Else
            Exit Do
        End If
    Loop

    StripMark = Trim(result)
End Function
