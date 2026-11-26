# Corbol apps

The Corbol apps installed on the same PC work together: an app opens another one with a file or a folder, sends it a command even when it is already open and gets the answer back. The AI assistant can use them too, always after you confirm.

The link is available from these versions: **CorbolImg 1.9.0**, **CorbolIp 1.13.0**, **CorbolPad 1.4.0**, **CorbolPrompt 0.5.0**, **CorbolSys 1.7.0**. An older app still opens, but does not receive commands: update it.

## In the menus
| App | Where | What it does |
|---|---|---|
| CorbolPad | *File → Open in* | opens the tab's file in another Corbol app |
| CorbolPrompt | ⋯ menu → *Open the folder in* | opens the tab's folder in another app (CorbolImg, ...) |
| CorbolImg | right-click the image → *Open in* | opens the image in another app |
| CorbolSys, CorbolIp | menu → *Corbol apps* | starts the other Corbol apps or brings them to the front |

The list shows only the installed apps that can open that file. If the app is already open, the file opens there (in a new tab); if it is closed, it starts.

## The AI assistant
In CorbolPad, CorbolPrompt and in the CorbolSys analysis the assistant knows which other Corbol apps are installed and can use them when you ask, for example:
- *"open this text in a CorbolPad tab"*
- *"prepare in CorbolPrompt the command to see the largest folders"*: the command is typed in a new tab but does **not** run, you run it with Enter
- *"ask CorbolSys how the PC is doing"*, *"how is the network?"* (CorbolIp)

Before doing anything the app shows what the assistant wants to do and asks **Go ahead?**. The result appears in the conversation and the assistant sees it with the next question.

## Commands
| App | Commands |
|---|---|
| CorbolPad | `open` files · `text` title, text: new tab with that text |
| CorbolPrompt | `open` folder: new tab there · `paste` command, [folder]: command typed but not run |
| CorbolImg | `open` images or a folder · `convert` files or folders: converter |
| CorbolSys | `status`: short PC status · `report`: full analysis (about 10 seconds) |
| CorbolIp | `status`: network status (the same text as *Copy all*) |

They all also answer `show` (brings the window to the front), `ping` (name and version) and `verbs` (the commands it knows).

## Security
- The link is a Windows *named pipe*, `CorbolLink.<App>.<user>.<session>`, open **only to your user** and closed to connections from the network.
- No command deletes or changes files; `paste` does not run the command.
- The actions asked by the assistant start only after you confirm.

## For developers
The code is in `CorbolLink.cs`, identical in all the apps. Request (UTF-8, one line per field, `\`, line feed and carriage return written as `\\`, `\n`, `\r`): `CORBOL/1`, the command, the number of arguments, one argument per line. Answer: `ok` or `err`, then the text.
