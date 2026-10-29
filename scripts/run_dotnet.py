from pathlib import Path
import os,subprocess,sys
root=Path(__file__).resolve().parent.parent;tc=root/'toolchain';env=os.environ.copy();env.update({'DOTNET_ROOT':str(tc/'dotnet'),'DOTNET_CLI_HOME':str(tc/'cli'),'NUGET_PACKAGES':str(tc/'packages'),'DOTNET_NOLOGO':'1','DOTNET_CLI_TELEMETRY_OPTOUT':'1','ANDROID_HOME':str(tc/'android-sdk'),'JAVA_HOME':r'C:\Program Files\Java\jdk-21.0.10'})
raise SystemExit(subprocess.call([str(tc/'dotnet/dotnet.exe'),*sys.argv[1:]],cwd=root,env=env))
