# MR-HomeAssistant

Unity project for the MR HomeAssistant prototype.

## Requirements

- Unity `6000.4.3f1`
- Git access to this repository
- Network access for Unity Package Manager to restore packages

## Open The Project

1. Clone the repository.
2. Open the project from Unity Hub with editor `6000.4.3f1`.
3. Wait for Package Manager to restore dependencies.
4. Install the platform build modules you need in Unity Hub if you plan to build for them.

## Repository Layout

The repository is intended to track the Unity project files required on both Windows and macOS:

- `Assets/`
- `Packages/`
- `ProjectSettings/`

Local machine output and IDE folders are ignored, including:

- `Library/`
- `Temp/`
- `Logs/`
- `UserSettings/`
- `.vs/`
- `.idea/`
- `.vscode/`

## Notes

- Use the same Unity editor version on both Windows and macOS to avoid package and project setting churn.
- Text-based Unity assets are normalized with `.gitattributes` to reduce line-ending noise across platforms.
