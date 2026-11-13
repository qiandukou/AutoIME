' ===========================================================================
'  AutoIME helper  --  read Photoshop's current state into a file
'
'  Why: AutoIME itself cannot call Photoshop's COM (security software blocks
'       unsigned programs and the activation hangs). The system's cscript
'       host is trusted, so we let it do the work; AutoIME only reads a file.
'
'  usage: cscript //nologo //B AutoIME.PsTool.vbs "<output file>" ["<ps.pid file>"]
'
'  Written values:
'     <tool name>   e.g. typeCreateOrEditTool / paintbrushTool
'     !busy         Photoshop refused to answer = it is busy, which in
'                   practice means a text edit session is active on canvas
'                   (PS locks its scripting engine while you type there)
'     !nops:G..C..  could not attach to Photoshop (G=GetObject error,
'                   C=CreateObject error)
'
'  IMPORTANT: the file is rewritten every ~1.5s even when nothing changed,
'  because AutoIME treats a stale file as "no information" and would lock the
'  IME again in the middle of a long typing session. (v1.0.5)
' ===========================================================================
Option Explicit

Dim fso, outPath, pidPath, last, state, haveApp, app, e1, e2, f, tick
Set fso = CreateObject("Scripting.FileSystemObject")

If WScript.Arguments.Count < 1 Then WScript.Quit 1
outPath = WScript.Arguments(0)
pidPath = ""
If WScript.Arguments.Count >= 2 Then pidPath = WScript.Arguments(1)

last = "~init~"
haveApp = False
e1 = 0
e2 = 0
tick = 0

Do
    state = ""
    tick = tick + 1

    On Error Resume Next

    ' ---- 1) normal way: attach to an instance already registered in the ROT
    If Not haveApp Then
        e1 = 0
        Set app = GetObject(, "Photoshop.Application")
        e1 = Err.Number
        Err.Clear
        If e1 = 0 Then
            haveApp = True
        Else
            ' ---- 2) fallback: CreateObject attaches to a running Photoshop,
            '         but would LAUNCH it when it is not running. Only do it
            '         while AutoIME says Photoshop is alive (pid file exists).
            If pidPath <> "" And fso.FileExists(pidPath) Then
                e2 = 0
                Set app = CreateObject("Photoshop.Application")
                e2 = Err.Number
                Err.Clear
                If e2 = 0 Then haveApp = True
            End If
        End If
    End If

    ' ---- 3) read the state
    If haveApp Then
        state = app.CurrentTool
        If Err.Number <> 0 Then
            ' 0x8001010A (RPC_E_SERVERCALL_RETRYLATER) = application busy
            ' keep haveApp: usually it just means a text edit session is active
            Err.Clear
            state = "!busy"
        End If
        If state = "" Then state = "!busy"
    Else
        state = "!nops:G" & e1 & "C" & e2
    End If

    On Error GoTo 0

    ' ---- 4) write on change, and refresh the timestamp every ~1.5s
    If state <> last Or (tick Mod 6) = 0 Then
        WriteTool outPath, state
        last = state
    End If

    WScript.Sleep 250
Loop

Sub WriteTool(p, s)
    Dim h
    On Error Resume Next
    Set h = fso.CreateTextFile(p, True)
    If Err.Number = 0 Then
        h.Write s
        h.Close
    End If
    Err.Clear
End Sub
