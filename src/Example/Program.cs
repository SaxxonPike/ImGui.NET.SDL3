using System.Numerics;
using ImGuiNET.SDL3;
using SDL;
using static SDL.SDL3;
using ImGui = ImGuiNET.ImGui;

if (SDL_Init(SDL_InitFlags.SDL_INIT_GAMEPAD |
             SDL_InitFlags.SDL_INIT_VIDEO |
             SDL_InitFlags.SDL_INIT_EVENTS) != 0)
    throw new Exception($"Failed to initialize SDL: {SDL_GetError()}");

unsafe
{
    try
    {
        SDL_Window* window = default;
        SDL_Renderer* renderer = default;

        if (SDL_CreateWindowAndRenderer(
                "ImGui.NET.SDL3 Example",
                1280,
                720,
                SDL_WindowFlags.SDL_WINDOW_RESIZABLE,
                &window,
                &renderer
            ) != 0)
            throw new Exception($"Failed to create window: {SDL_GetError()}");

        ImGui.CreateContext();
        ImGuiSdl3.Init(window, renderer);
        SDL_Event ev = default;
        var done = false;
        var clearColor = new Vector4(0.45f, 0.55f, 0.60f, 1.00f);

        while (!done)
        {
            while (SDL_PollEvent(&ev) != 0)
            {
                ImGuiSdl3.ProcessEvent(&ev);
                if (ev.Type == SDL_EventType.SDL_EVENT_QUIT)
                    done = true;
                else if (ev.Type == SDL_EventType.SDL_EVENT_WINDOW_CLOSE_REQUESTED &&
                         ev.window.windowID == SDL_GetWindowID(window))
                    done = true;
            }

            if ((SDL_GetWindowFlags(window) & SDL_WindowFlags.SDL_WINDOW_MINIMIZED) != 0)
            {
                SDL_Delay(10);
                continue;
            }

            ImGuiSdl3.NewFrame();
            ImGui.NewFrame();

            ImGui.ShowDemoWindow();

            ImGui.Render();

            if (SDL_SetRenderDrawColorFloat(renderer, clearColor.X, clearColor.Y, clearColor.Z, clearColor.W) != 0)
                throw new Exception($"Failed to set render draw color: {SDL_GetError()}");

            if (SDL_RenderClear(renderer) != 0)
                throw new Exception($"Failed to clear: {SDL_GetError()}");

            ImGuiSdl3.RenderDrawData(ImGui.GetDrawData());

            if (SDL_RenderPresent(renderer) != 0)
                throw new Exception($"Failed to present: {SDL_GetError()}");
        }

        ImGuiSdl3.Shutdown();
        ImGui.DestroyContext();
    }
    finally
    {
        SDL_Quit();
    }
}