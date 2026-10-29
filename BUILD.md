# Build and signing

## This machine

The isolated toolchain is already installed under `toolchain/`: .NET SDK 10.0.403 with Android workload 36.1.2, Android platform API 36, build-tools 36.0.0 and platform-tools. JDK 21 is at `C:/Program Files/Java/jdk-21.0.10`. This setup did not replace the Windows .NET 8 SDK or change Windows source.

From this project directory, in PowerShell:

```powershell
./build.ps1 -JavaSdkDirectory 'C:/Program Files/Java/jdk-21.0.10'
./test.ps1
```

APK output: `artifacts/YORU-Android-V9.3.apk`. `build.ps1` uses the explicit `SignAndroidPackage` target, restores from the local offline feed when present and stops on failures. This SDK's plain managed Build/Publish invocation alone is not the delivery command.

## Fresh Windows setup

Install Python 3 and Microsoft/OpenJDK 21, then run:

```powershell
./setup.ps1
./build.ps1 -JavaSdkDirectory 'C:/path/to/jdk-21'
./test.ps1
```

Setup downloads an isolated SDK using the pinned Microsoft release hash, fills an official NuGet package feed, and installs verified Android archives. Expect several gigabytes of disk space and substantial downloads. It does not install a virtualization driver or alter Windows application files. Standard alternative: install .NET 10, run `dotnet workload install android`, and install Android API 36 plus build-tools 36.0.0 through Android Studio or `InstallAndroidDependencies`; pass their paths to `build.ps1`. Google Android SDK use is subject to its license terms.

The bundled local feed was necessary here because the host's .NET TLS stack cannot acquire Schannel credentials. Download helpers use Python HTTPS with certificate validation, and workload/package installs use the local feed. This is a host-build workaround, not a TLS workaround inside the Android app. Test-only live requests use the Node HTTPS bridge; Node is not bundled or required on Android.

## Release signing for distribution

The delivered APK verifies with v2/v3 signatures but uses `CN=Android Debug`. Use a private release key before publishing or distributing as a production app. Do not put signing passwords or a private keystore in a public source archive. Pass `AndroidKeyStore=true`, `AndroidSigningKeyStore`, `AndroidSigningKeyAlias`, `AndroidSigningKeyPass` and `AndroidSigningStorePass` through your secure build environment, then rebuild with `SignAndroidPackage`. Preserve the chosen keystore for future updates: a differently signed APK cannot update this installation without uninstalling it, which removes local app data.

The source archive excludes the SDK, package cache, build outputs and signing keys. AndroidX packages are pinned in the project. Shared core code is compiled into Android through linked source items and is also compiled as a portable .NET library for tests.

References: [Microsoft Android setup](https://learn.microsoft.com/dotnet/android/getting-started/installation/net-android), [Android SDK/JDK dependencies](https://learn.microsoft.com/dotnet/android/getting-started/installation/dependencies), [Android app signing](https://developer.android.com/studio/publish/app-signing).
