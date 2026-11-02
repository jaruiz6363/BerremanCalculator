# Building from source

This page builds BerremanCalculator from its source code: the native engine, the three programs
(the GUI, the command line `berreman` and the MCP server), and the tests.

**To use the program, you will not need to build it.** Installers for Windows, macOS (Apple
Silicon) and Linux will be published on the
[Releases](https://github.com/jaruiz6363/BerremanCalculator/releases) page from the first release.
They include everything: the three programs, the engine, .NET, the samples, the documents and the
examples. There is no release yet, so for now this page is how to get the program. After the first
release, you need it only to change the program, or to run it on a system that has no installer.

There are two parts, built in this order:
1. **The engine** (`native/`), in C++, built with CMake. It is a shared library,
   `berreman_native` (`.dll` on Windows, `.so` on Linux, `.dylib` on macOS).
2. **The programs and tests** (`managed/`), in C#, built with the .NET SDK. Each program copies the
   engine library from `build/` beside itself, so the engine must be there first.

**Changing only the C# code** (the GUI, the command line, the API, the MCP server) needs no C++
compiler and no CMake. Use the engine library an installer already put on your computer instead of
building it: see [Changing only the C# code](#changing-only-the-c-code).

## What to install

| | Windows | macOS | Linux |
|---|---|---|---|
| Git | [git-scm.com](https://git-scm.com) | `xcode-select --install` | `sudo apt install git` |
| A C++17 compiler | Visual Studio 2022 or later, with the workload **Desktop development with C++** | the Xcode command-line tools (as above) | `sudo apt install build-essential` |
| CMake 3.20 or later | included with that Visual Studio workload, or [cmake.org](https://cmake.org) | `brew install cmake` | `sudo apt install cmake` |
| .NET SDK 8 or later | [dot.net](https://dot.net) | [dot.net](https://dot.net) | [dot.net](https://dot.net) |

The Linux commands are for Ubuntu and Debian; other distributions have the same packages under
their own installers. Check what you have with `git --version`, `cmake --version` and
`dotnet --version`.

## 1. Get the source

```
git clone https://github.com/jaruiz6363/BerremanCalculator.git
cd BerremanCalculator
```

Every command below is run from this folder.

## 2. Build and test the engine

```
cmake -S . -B build
cmake --build build --config Release
ctest --test-dir build -C Release
```

- The first command configures the build into the folder `build/`. On Windows it finds Visual
  Studio itself.
- The second compiles. Every compiler warning is treated as an error, so a clean build prints no
  warnings.
- The third runs the engine's tests. It ends with `100% tests passed`.

The engine library is then in `build/Release/` on Windows, and in `build/` on macOS and Linux.
The same build makes the OpticStudio surface DLL, `US_BERREMAN.dll`
([examples/zemax-uds](../examples/zemax-uds/README.md)), so its arithmetic is tested on every
platform.

One of the engine's tests, `constraints`, checks the rules native code follows: no memory
allocation, no `float`, no fixed-size arrays, no exceptions ([engine design](engine-design.md)).
A change that breaks one of them fails here.

## 3. Build the programs

```
dotnet build managed/BerremanCalculator.sln -c Release
```

It ends with `0 Warning(s)` and `0 Error(s)`. The first build downloads the GUI's packages
(Avalonia) from NuGet, so it needs the internet once; everything else builds offline.

## 4. Run the tests

```
dotnet run --project managed/Berreman.Native.Tests -c Release
dotnet run --project managed/Berreman.Gui.Tests -c Release
```

- **Berreman.Native.Tests** checks the engine through the C# API: the literature samples against
  their papers, closed forms, the file formats, the MCP server and the OpticStudio surface. It
  ends with a line such as `4257 checks, 0 failures`.
- **Berreman.Gui.Tests** drives the window as a person would, with real mouse input, but without
  showing it (headless). It ends the same way.

Each prints `FAIL` and what was expected for any check that fails, and exits with a non-zero code.

## 5. Run the programs

```
dotnet run --project managed/Berreman.Gui -c Release
dotnet run --project managed/Berreman.Cli -c Release -- evaluate samples/literature/mansuripur-1990/quadrilayer.berr
dotnet run --project managed/Berreman.Mcp -c Release
```

The first opens the GUI. The second evaluates a sample with the command line and prints a table
(everything after `--` goes to `berreman`; `-- help` lists its commands). The third starts the MCP
server, which waits for messages on its standard input; it is meant to be started by an AI
assistant, not typed into.

The built programs are also in `managed/<project>/bin/Release/net8.0/`, for example
`managed/Berreman.Cli/bin/Release/net8.0/berreman.exe`, and can be run from there.

## Changing only the C# code

Skip the C++ compiler, CMake and step 2. Copy the engine library from an installed program into a
folder `build` in the source folder, then carry on with step 3:

| | The engine library | Where an installed program has it |
|---|---|---|
| Windows | `berreman_native.dll` | the folder you installed it in. By default that is `%LOCALAPPDATA%\Programs\BerremanCalculator` (installed for you) or `C:\Program Files\BerremanCalculator` (for all users) |
| macOS | `libberreman_native.dylib` | `BerremanCalculator.app/Contents/MacOS` (in Finder, Show Package Contents) |
| Linux | `libberreman_native.so` | inside the AppImage: `./BerremanCalculator-<version>-x86_64.AppImage --appimage-extract`, then `squashfs-root/usr/lib/berremancalculator` |

For example, on Windows, in a Command Prompt (`cmd`):

```
mkdir build
copy "%LOCALAPPDATA%\Programs\BerremanCalculator\berreman_native.dll" build\
dotnet build managed/BerremanCalculator.sln -c Release
```

- **Use the engine of the same version as your source.** The C interface between the engine and
  the C# code may still change between versions. If they do not match, the programs say so: "ABI
  mismatch: the native engine is version …, this program expects …".
- **The tests skip the OpticStudio surface.** Its DLL is built only by the engine's CMake build, so
  Berreman.Native.Tests prints `SKIPPED  the OpticStudio surface's checks` and runs all its other
  checks. The GUI tests run as usual.

## Optional parts

**The OpticStudio surface in Visual Studio.** On Windows, `examples/zemax-uds/us_berreman.sln`
builds the same DLL as CMake, without CMake. Its [README](../examples/zemax-uds/README.md) gives the
steps. A test checks that the two builds compile the same files.

**The Optiland example's tests.** They need Python with Optiland and pytest, the engine from step 2
and the command line from step 3. With [uv](https://docs.astral.sh/uv/):

```
uv run --no-project --with optiland==0.6.3 --with pytest python -m pytest -q examples/optiland/test_berreman_optiland.py
```

It ends with `35 passed`. Without the command line, some checks are skipped rather than failed.
See [examples/optiland](../examples/optiland/README.md).

**The user guide's pictures.** They are taken from the program itself, headless:

```
dotnet run --project managed/Berreman.Gui.Tests -c Release -- --guide docs/images
```

It drives the window through each scene of [the user guide](user-guide.md) and writes the PNG
files. Run it after a change to the window, so the pictures match it.

## When something goes wrong

- **"the native engine (berreman_native) did not load".** The engine was not built, or was built
  after the C# projects. Do step 2, then step 3 again.
- **An old engine is used after a change to `native/`.** The programs copy whichever
  `berreman_native` they find under `build/`. If you have built both Debug and Release, delete the
  one you are not using, or delete `build/` and do step 2 again.
- **A warning stops the engine build.** Warnings are errors on purpose: fix the cause.
- **Warning MSB8029 on Windows** ("cannot reside under the Temporary directory"). The source is in
  a folder under %TEMP%. The build still works; to be rid of it, clone somewhere else, such as
  `C:Source`.
- **The GUI build cannot restore packages.** It needs the internet the first time, to download
  Avalonia from NuGet.

## Installers

The Windows installer, the macOS app and the Linux AppImage are built by scripts kept in a separate
repository. They build from a clean copy of this one at a given commit, run every test above
(the Optiland ones included), publish the three programs self-contained (no .NET to install) and
package them. A program built here with `dotnet build` needs the .NET runtime on the computer that
runs it; `dotnet publish` with `--self-contained` makes one that does not.
