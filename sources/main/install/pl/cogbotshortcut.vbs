strExePath = WScript.Arguments(0)
strIconPath = WScript.Arguments(1)
strWorkDir = WScript.Arguments(2)

set WshShell = WScript.CreateObject("WScript.Shell" )
strDesktop = WshShell.SpecialFolders("AllUsersDesktop" )
set oShellLink = WshShell.CreateShortcut(strDesktop & "\Cogbot.lnk" )
oShellLink.TargetPath = strExePath
oShellLink.WindowStyle = 1
oShellLink.IconLocation = strIconPath
oShellLink.Description = "Shortcut For Cogbot"
oShellLink.WorkingDirectory = strWorkDir
oShellLink.Save 

Set outputfileObject = CreateObject("Scripting.FileSystemObject")
if not outputfileObject.FolderExists("C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Cogbot") then
    outputfileObject.CreateFolder("C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Cogbot")
end if

set oStartLink = WshShell.CreateShortcut("C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Cogbot\Cogbot.lnk" )
oStartLink.TargetPath = strExePath
oStartLink.WindowStyle = 1
oStartLink.IconLocation = strIconPath
oStartLink.Description = "Shortcut For Cogbot"
oStartLink.WorkingDirectory = strWorkDir
oStartLink.Save 