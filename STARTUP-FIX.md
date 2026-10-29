# Android V9.3 startup compatibility build (90301)

Reported device: Redmi Note 8 Pro, Android 11; closes before the first screen.

The Release project previously enabled .NET Android JNI marshal methods by default. This build disables `AndroidEnableMarshalMethods` and uses runtime JNI registration instead. The generated ApplicationRegistration.java now explicitly registers YoruApp, including onCreate and onTrimMemory, before application initialization. This follows the workaround for the .NET 10 startup regression reported at https://github.com/dotnet/maui/issues/35209. This is a targeted compatibility fix, not a confirmed diagnosis of the user's device: its crash log has not been supplied.

The package identifier and signing certificate remain unchanged; versionCode increases from 90300 to 90301. Install the new APK as an update over the existing app. Do not uninstall or clear storage: an update preserves local settings/history. Windows sources and streaming code are unchanged.

Validation: clean Release APK build, APK signature/package checks, generated JNI-registration inspection, and portable core checks. No connected Android device was available; both local software emulator versions failed to boot. Launch and playback on the Redmi remain to be confirmed.

If the app still closes, connect the phone with USB debugging and run these from this project in PowerShell:

```powershell
$adb = './toolchain/android-sdk/platform-tools/adb.exe'
& $adb logcat -c
& $adb shell am force-stop app.yoru.android
& $adb shell monkey -p app.yoru.android -c android.intent.category.LAUNCHER 1
# After the app closes:
& $adb logcat -d -b crash -v threadtime > yoru-crash.txt
```

Share yoru-crash.txt to identify the exact exception. Crash logs may include device or network details; review before sharing.
