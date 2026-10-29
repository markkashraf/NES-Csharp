# NesEmu

A small NES emulator written in C# with Windows Forms.

## Features
- Loads `.nes` ROMs
- Supports mappers 0, 1, 2, and 3
- CPU + PPU emulation
- Controller input via keyboard
- Displays the game in a window

## Run
```bash
dotnet build
# or run directly

dotnet run --project NesEmu.csproj
```

If no ROM path is provided, the app opens a file dialog to choose a game.

## Controls
- `Z` = A
- `X` = B
- `Shift` / `Backspace` = Select
- `Enter` = Start
- Arrow keys = D-pad
