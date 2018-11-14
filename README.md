# EWinRM

Epic replacement for WinRM. Fast cloud native infrastructure provisioning.

Problem: you want to do something (provision infra, run tests etc.) on remote machine.

Telnet, ftp, or ssh are not available (because it's a crusty windows machine). You are too busy and dynamic to deal with WinRM.

This is a single 8k exe that does the following:

- Set up a TCP socket that accepts connections

- You can either send text content or a zip file to the socket (e.g. with netcat or tool of your choosing)

- If the content is zip file, it gets unzipped and a script called autoexec.cmd in zip root gets run. This script
  is supposed to copy the things from the zip file to wherever, and generally do the needfull (unzip things, install services,...)

- If the content is not zip file, it's interpreted as .cmd file and gets run. You are expected to trigger small commands from this
  file.




