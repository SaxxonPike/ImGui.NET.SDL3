# ImGui.NET.SDL3

Adapts the `ppy.SDL3-CS` package to the `ImGui.NET` package.

Much of the code is based off the adapter written into the SDL3 library.
The `ImGui.NET` package does not have this enabled within bindings or binaries,
so this is a C# backend implementation written to close the gap.

This does not (yet) support multiple ImGui contexts.
