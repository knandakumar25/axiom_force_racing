# Unity exports

Only run after the owner requests an explicit target. Use a local, opt-in Docker export service with Unity's official CLI and Headless Editor. Keep it outside the default Compose profiles and GitHub Actions. The Windows Unity CLI handles other Unity operations. The current Windows Editor is 6.3 and the official Headless Editor requires 6.7 or later. Before exporting, align the project and Editor versions, verify the official base image pull and configure a floating license. Configure Unity build profiles after the project exists. The export service defaults to help. It requires a Unity project in Game/ and the corresponding Unity build profile in Assets/Settings/Build Profiles/. The profile names are Linux, Windows, Web, Android and macOS.

| Target | Official Linux container module or release step |
| --- | --- |
| Linux | linux-il2cpp |
| Web | webgl, with FFmpeg for AAC import |
| Android | android, with matching SDK, NDK and JDK |
| Windows | windows-coreclr |
| macOS | mac-coreclr, followed by Mac signing/notarization |
| iOS | Unity's Linux container does not support the iOS module. Establish an owner-approved native Mac exception before generating or archiving an Xcode project. |

[Official target and licensing requirements](https://docs.unity.com/en-us/engine/6000.7/manual/building-and-publishing/docker/requirements-reference)
