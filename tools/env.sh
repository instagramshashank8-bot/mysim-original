# Source this before any build or run: keeps every cache, temp file and download on D:.
R=/d/MySim-Original/.cache; W='D:\MySim-Original\.cache'
export TEMP="$W\temp" TMP="$W\temp" APPDATA="$W\appdata" LOCALAPPDATA="$W\localappdata"
export NUGET_PACKAGES="$W\nuget" NUGET_HTTP_CACHE_PATH="$W\nuget-http" NUGET_PLUGINS_CACHE_PATH="$W\nuget-plugins"
export DOTNET_CLI_HOME="$W\dotnet" DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
export BLENDER_USER_RESOURCES="$W\blender" PYTHONDONTWRITEBYTECODE=1
export GODOT="/c/Users/shash/AppData/Local/Microsoft/WinGet/Packages/GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe"
export BLENDER=/d/MySim-Original/.cache/blender-app/blender.exe
