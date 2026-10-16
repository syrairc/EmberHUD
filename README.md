# EmberHUD

Read-only overlay with plugins. Windows x64, needs the .NET 10 runtime.

Run `EmberLauncher.exe`. First run (or if it can't find the game) it opens Setup - pick the game folder, hit Extract once to build the icons, then Save & Launch. After that the launcher just starts the game and the overlay. Log is in `Logs\launcher.log`. `Ember.exe --fake` runs the overlay on fake data with no game.

Steam and standalone installs are both detected. Standalone the launcher starts the game exe itself, Steam it asks Steam to.

The game exe asks for admin, and an elevated game can't be read by a normal overlay (access denied, error 5). So the game has to run as a normal user, via the `RunAsInvoker` compat flag. Standalone the launcher sets it on the process it starts, nothing touches the registry. Steam starts the exe itself so the flag goes on the exe: `HKCU\SOFTWARE\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers`, value name = full path of the game exe, data `~ RUNASINVOKER`. The launcher writes it before each Steam launch, Setup has a `Run without admin` button, or import `Steam-RunWithoutAdmin-Apply.reg` yourself (`Remove` undoes it). The .reg files assume the default Steam library path, edit it if yours is elsewhere.

`Run as administrator` in Setup starts the overlay elevated (UAC) and removes the Steam flag again, so the game is elevated too.

Limited user, standalone only - pick a local Windows account under `Run as` and the launcher starts the game as that account with `runas /savecred`. Make the account yourself, then hit `Save password` once so Windows keeps it. The overlay and any extra programs always run as you.

After a game update hit Extract again to get new icons if needed.
