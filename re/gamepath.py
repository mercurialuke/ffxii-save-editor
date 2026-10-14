# Location of FFXII_TZA.exe for the analysis scripts. Set FFXII_EXE if the game isn't in Steam's default library.
import os

EXE = os.environ.get("FFXII_EXE") or os.path.join(
    os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)"),
    "Steam", "steamapps", "common", "FINAL FANTASY XII THE ZODIAC AGE", "x64", "FFXII_TZA.exe")
