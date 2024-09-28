using System.Numerics;
using ImGuiNET;
using ImGuiNET.SDL3;
using SDL;
using static SDL.SDL3;
using ImGui = ImGuiNET.ImGui;

var imGuiContext = IntPtr.Zero;
var sdlInitialized = false;

//
// In the event the application crashes for any reason, this will ensure that
// any resources used by SDL or ImGui are released.
//

AppDomain.CurrentDomain.UnhandledException += (_, _) => ShutDown();

//
// Initialize SDL. Video and Events are mandatory for ImGui functionality, but
// Gamepad is included here for demonstration.
//

if (SDL_Init(SDL_InitFlags.SDL_INIT_GAMEPAD |
             SDL_InitFlags.SDL_INIT_VIDEO |
             SDL_InitFlags.SDL_INIT_EVENTS) == 0)
    throw new Exception($"Failed to initialize SDL: {SDL_GetError()}");

sdlInitialized = true;

unsafe
{
    //
    // Create the SDL window.
    //

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

    //
    // Create the ImGui context and set it as current.
    //

    imGuiContext = ImGui.CreateContext();
    ImGui.SetCurrentContext(imGuiContext);
    ImGuiSdl3.Init(window, renderer);

    //
    // Configure the ImGui context.
    //

    var io = ImGui.GetIO();
    io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard |
                      ImGuiConfigFlags.NavEnableGamepad;

    //
    // Start reading all current gamepads.
    //

    var gamepads = new Dictionary<SDL_JoystickID, IntPtr>();
    var gamepadIds = SDL_GetGamepads()!;
    for (var i = 0; i < gamepadIds.Count; i++)
        gamepads[gamepadIds[i]] = (IntPtr)SDL_OpenGamepad(gamepadIds[i]);

    //
    // Set up some parameters for the event loop.
    //

    SDL_Event ev = default;
    var done = false;
    var clearColor = new Vector4(0.45f, 0.55f, 0.60f, 1.00f);

    if (SDL_SetRenderVSync(renderer, 1) != 0)
        throw new Exception($"Failed to set Vsync: {SDL_GetError()}");

    //
    // Main event loop.
    //

    while (!done)
    {
        //
        // Process pending SDL events.
        //

        while (SDL_PollEvent(&ev) != 0)
        {
            ImGuiSdl3.ProcessEvent(&ev);

            //
            // Handle some events from SDL.
            //

            switch (ev.Type)
            {
                //
                // Handle when the app should close.
                //

                case SDL_EventType.SDL_EVENT_QUIT:
                case SDL_EventType.SDL_EVENT_WINDOW_CLOSE_REQUESTED when ev.window.windowID == SDL_GetWindowID(window):
                    done = true;
                    break;

                //
                // Handle when gamepads connect.
                //

                case SDL_EventType.SDL_EVENT_GAMEPAD_ADDED:
                    gamepads[ev.gdevice.which] = (IntPtr)SDL_OpenGamepad(ev.gdevice.which);
                    break;

                //
                // Handle when gamepads disconnect.
                //

                case SDL_EventType.SDL_EVENT_GAMEPAD_REMOVED:
                    if (gamepads.TryGetValue(ev.gdevice.which, out var gamepad))
                        SDL_CloseGamepad((SDL_Gamepad*)gamepad);
                    break;
            }
        }

        //
        // Skip processing frames while minimized.
        //

        if ((SDL_GetWindowFlags(window) & SDL_WindowFlags.SDL_WINDOW_MINIMIZED) != 0)
        {
            SDL_Delay(10);
            continue;
        }

        //
        // Indicate to ImGui that we intend to start a new frame. It is important to know
        // that draw calls to ImGui do not actually render anything to SDL directly. That said,
        // all draw calls to ImGui should be made between "NewFrame" and "Render".
        //

        ImGuiSdl3.NewFrame();
        ImGui.NewFrame();

        //
        // Here is where we will render all our ImGui frames. You can put anything you like
        // in this section, but for our purposes, we will just render the demo window.
        //

        ImGui.ShowDemoWindow();

        //
        // Indicate to ImGui that we have finished our frame.
        //

        ImGui.Render();

        //
        // Clear the render buffer. This begins our per-frame draw calls to SDL.
        //

        if (SDL_SetRenderDrawColorFloat(renderer, clearColor.X, clearColor.Y, clearColor.Z, clearColor.W) != 0)
            throw new Exception($"Failed to set render draw color: {SDL_GetError()}");

        if (SDL_RenderClear(renderer) != 0)
            throw new Exception($"Failed to clear: {SDL_GetError()}");

        //
        // Actually render ImGui's draw lists to SDL. Ideally, any other SDL render calls will
        // be done prior to this call.
        //

        ImGuiSdl3.RenderDrawData(ImGui.GetDrawData());

        //
        // Presents the frame buffer to the window.
        //

        if (SDL_RenderPresent(renderer) != 0)
            throw new Exception($"Failed to present: {SDL_GetError()}");
    }
}

void ShutDown()
{
    //
    // Shuts down ImGui.
    //

    if (imGuiContext != IntPtr.Zero)
    {
        ImGuiSdl3.Shutdown();
        ImGui.DestroyContext(imGuiContext);
        imGuiContext = IntPtr.Zero;
    }

    //
    // Shuts down SDL.
    //

    if (sdlInitialized)
    {
        SDL_Quit();
        sdlInitialized = false;
    }
}
