using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET.SDL3;
using SDL;
using static SDL.SDL3;

// ReSharper disable once CheckNamespace

namespace ImGuiNET.SDL3;

public static unsafe class ImGuiSdl3
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ImGuiUserCallback(
        ImDrawList* cmdList,
        ImDrawCmd* drawCmd);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ImGuiPlatformSetImeDataCallback(
        IntPtr ctx,
        ImGuiViewport* viewport,
        ImGuiPlatformImeData* data);

    private const short StickDeadZone = 8192;
    private const float StickActiveZone = 32768 - StickDeadZone;

    private static bool _begun;
    private static IntPtr _ctx;
    private static readonly SDL_Cursor*[] Cursors = new SDL_Cursor*[(int)ImGuiMouseCursor.COUNT];
    private static SDL_Texture* _texture;
    private static ulong _lastTime;
    private static Vector4 _renderRect;
    private static SDL_Renderer* _renderer;
    private static SDL_Window* _window;
    private static ImGuiPlatformSetImeDataCallback? _platformSetImeDataCallback;
    private static SDL_Window* _imeWindow;
    private static bool _initted;

    public static void Init(SDL_Window* window, SDL_Renderer* renderer)
    {
        if (_initted)
            throw new Exception("This backend does not work with multiple contexts");
        _initted = true;
        
        _renderer = renderer;
        _window = window;

        var io = ImGui.GetIO();
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset |
                           ImGuiBackendFlags.HasGamepad |
                           ImGuiBackendFlags.HasMouseCursors;

        var vp = ImGui.GetMainViewport();
        vp.PlatformHandle = (IntPtr)window;

        _platformSetImeDataCallback = PlatformSetImeData;
        io.PlatformSetImeDataFn = Marshal.GetFunctionPointerForDelegate(_platformSetImeDataCallback);

        CreateFontTexture();
    }

    public static void Shutdown()
    {
        if (!_initted)
            return;
        if (_texture != null)
        {
            SDL_DestroyTexture(_texture);
            _texture = null;
        }

        for (var i = 0; i < Cursors.Length; i++)
        {
            if (Cursors[i] == null)
                continue;

            SDL_DestroyCursor(Cursors[i]);
            Cursors[i] = null;
        }

        _ctx = IntPtr.Zero;
        _platformSetImeDataCallback = null;
        _initted = false;
    }

    public static void NewFrame()
    {
        if (_begun || !_initted)
            return;

        var io = ImGui.GetIO();
        var now = SDL_GetTicksNS();
        var elapsed = now - _lastTime;

        int width, height;
        SDL_GetRenderOutputSize(_renderer, &width, &height)
            .AssertSdlZero();

        float scaleX, scaleY;
        SDL_GetRenderScale(_renderer, &scaleX, &scaleY)
            .AssertSdlZero();

        SDL_FRect renderRect;
        SDL_GetRenderLogicalPresentationRect(_renderer, &renderRect)
            .AssertSdlZero();

        _renderRect = *(Vector4*)&renderRect;

        io.DeltaTime = elapsed;
        io.DisplaySize.X = width;
        io.DisplaySize.Y = height;
        io.DisplayFramebufferScale.X = scaleX;
        io.DisplayFramebufferScale.Y = scaleY;

        _begun = true;
        ImGui.NewFrame();

        _lastTime = now;
    }

    public static void RenderDrawData(ImDrawDataPtr drawData)
    {
        if (!_begun || !_initted)
            return;

        _begun = false;

        var hasViewport = SDL_RenderViewportSet(_renderer) == SDL_bool.SDL_TRUE;
        var hasClipRect = SDL_RenderClipEnabled(_renderer) == SDL_bool.SDL_TRUE;
        SDL_Rect oldViewport = default;
        SDL_Rect oldClipRect = default;

        if (hasViewport)
        {
            SDL_GetRenderViewport(_renderer, &oldViewport)
                .AssertSdlZero();
        }

        if (hasClipRect)
        {
            SDL_GetRenderClipRect(_renderer, &oldClipRect)
                .AssertSdlZero();
        }

        Render(_renderer, drawData);

        if (hasViewport)
        {
            SDL_SetRenderViewport(_renderer, &oldViewport)
                .AssertSdlZero();
        }
        else
        {
            SDL_SetRenderViewport(_renderer, null)
                .AssertSdlZero();
        }

        if (hasClipRect)
        {
            SDL_SetRenderClipRect(_renderer, &oldClipRect)
                .AssertSdlZero();
        }
        else
        {
            SDL_SetRenderClipRect(_renderer, null)
                .AssertSdlZero();
        }
    }

    public static void ProcessEvent(SDL_Event* ev)
    {
        if (!_initted)
            return;

        var io = ImGui.GetIO();

        switch (ev->Type)
        {
            case < SDL_EventType.SDL_EVENT_FIRST or > SDL_EventType.SDL_EVENT_LAST:
                break;
            case SDL_EventType.SDL_EVENT_GAMEPAD_BUTTON_UP:
            {
                if (ConvertGamepadButtonEvent(ev->gbutton) is not { } nav || nav == ImGuiKey.None)
                    break;

                io.AddKeyEvent(nav, false);
                break;
            }
            case SDL_EventType.SDL_EVENT_GAMEPAD_BUTTON_DOWN:
            {
                if (ConvertGamepadButtonEvent(ev->gbutton) is not { } nav || nav == ImGuiKey.None)
                    break;

                io.AddKeyEvent(nav, true);
                break;
            }
            case SDL_EventType.SDL_EVENT_GAMEPAD_AXIS_MOTION:
            {
                var (nav1, value1, nav2, value2) = ConvertGamepadAxisEvent(ev->gaxis);
                if (nav1 != null)
                    io.AddKeyAnalogEvent(nav1.Value, value1 != 0, value1);
                if (nav2 != null)
                    io.AddKeyAnalogEvent(nav2.Value, value2 != 0, value2);
                break;
            }
            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP:
            {
                if (ConvertMouseButtonEvent(ev->button) is not { } nav)
                    break;

                io.AddMouseButtonEvent((int)nav, false);
                break;
            }
            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN:
            {
                if (ConvertMouseButtonEvent(ev->button) is not { } nav)
                    break;

                io.AddMouseButtonEvent((int)nav, true);
                break;
            }
            case SDL_EventType.SDL_EVENT_MOUSE_MOTION:
            {
                var evCopy = *ev;
                SDL_ConvertEventToRenderCoordinates(_renderer, &evCopy)
                    .AssertSdlZero();

                io.AddMouseSourceEvent(ev->wheel.which == SDL_TOUCH_MOUSEID 
                    ? ImGuiMouseSource.TouchScreen 
                    : ImGuiMouseSource.Mouse);

                io.AddMousePosEvent(evCopy.motion.x, evCopy.motion.y);
                Console.WriteLine("{0},{1}", evCopy.motion.x, evCopy.motion.y);
                break;
            }
            case SDL_EventType.SDL_EVENT_MOUSE_WHEEL:
            {
                io.AddMouseSourceEvent(ev->wheel.which == SDL_TOUCH_MOUSEID 
                    ? ImGuiMouseSource.TouchScreen 
                    : ImGuiMouseSource.Mouse);

                io.AddMouseWheelEvent(ev->wheel.x, ev->wheel.y);
                break;
            }
            case SDL_EventType.SDL_EVENT_KEY_UP:
            {
                if (ConvertKeyboardEventKey(ev->key) is not { } nav || nav == ImGuiKey.None)
                    break;

                UpdateKeyboardModifiers(ev->key.mod);
                io.AddKeyEvent(nav, false);
                break;
            }
            case SDL_EventType.SDL_EVENT_KEY_DOWN:
            {
                if (ConvertKeyboardEventKey(ev->key) is not { } nav || nav == ImGuiKey.None)
                    break;

                UpdateKeyboardModifiers(ev->key.mod);
                io.AddKeyEvent(nav, true);
                break;
            }
            case SDL_EventType.SDL_EVENT_TEXT_INPUT:
            {
                if (ev->text.GetText() is not { } text || string.IsNullOrEmpty(text))
                    break;

                io.AddInputCharactersUTF8(text);
                break;
            }
        }
    }

    private static void PlatformSetImeData(IntPtr ctx, ImGuiViewport* viewport, ImGuiPlatformImeData* data)
    {
        var window = (SDL_Window*)viewport->PlatformHandle;
        if ((data->WantVisible == 0 || _imeWindow != window) && _imeWindow != null)
        {
            SDL_StopTextInput(_imeWindow)
                .AssertSdlZero();
            _imeWindow = null;
        }

        if (data->WantVisible == 0)
            return;

        var r = new SDL_Rect
        {
            x = (int)data->InputPos.X,
            y = (int)data->InputPos.Y,
            w = 1,
            h = (int)data->InputLineHeight
        };

        SDL_SetTextInputArea(window, &r, 0)
            .AssertSdlZero();

        SDL_StartTextInput(window)
            .AssertSdlZero();

        _imeWindow = window;
    }

    private static float ScaleGamepadAxisValue(short raw) =>
        raw switch
        {
            >= -StickDeadZone and <= StickDeadZone => 0,
            < 0 => (raw + StickDeadZone) / StickActiveZone,
            > 0 => (raw - StickDeadZone) / StickActiveZone
        };

    private static (ImGuiKey? Input1, float Value1, ImGuiKey? Input2, float Value2) ConvertGamepadAxisValue(
        SDL_GamepadAxisEvent ev,
        ImGuiKey negative,
        ImGuiKey positive) =>
        ScaleGamepadAxisValue(ev.value) switch
        {
            var x and < 0 => (negative, -x, positive, 0),
            var x and > 0 => (positive, x, negative, 0),
            _ => (positive, 0, negative, 0)
        };

    private static (ImGuiKey? Input1, float Value1, ImGuiKey? Input2, float Value2) ConvertGamepadAxisEvent(
        SDL_GamepadAxisEvent ev) =>
        ev.Axis switch
        {
            < SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX or >= SDL_GamepadAxis.SDL_GAMEPAD_AXIS_MAX =>
                default,
            SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX =>
                ConvertGamepadAxisValue(ev, ImGuiKey.GamepadLStickLeft, ImGuiKey.GamepadLStickRight),
            SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY =>
                ConvertGamepadAxisValue(ev, ImGuiKey.GamepadLStickUp, ImGuiKey.GamepadLStickDown),
            SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTX =>
                ConvertGamepadAxisValue(ev, ImGuiKey.GamepadRStickLeft, ImGuiKey.GamepadRStickRight),
            SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTY =>
                ConvertGamepadAxisValue(ev, ImGuiKey.GamepadRStickUp, ImGuiKey.GamepadRStickDown),
            SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER =>
                ConvertGamepadAxisValue(ev, ImGuiKey.GamepadL2, ImGuiKey.GamepadL2),
            SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER =>
                ConvertGamepadAxisValue(ev, ImGuiKey.GamepadR2, ImGuiKey.GamepadR2)
        };

    private static ImGuiKey? ConvertGamepadButtonEvent(SDL_GamepadButtonEvent ev) =>
        ev.Button switch
        {
            < SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH or >= SDL_GamepadButton.SDL_GAMEPAD_BUTTON_MAX => null,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH => ImGuiKey.GamepadFaceDown,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST => ImGuiKey.GamepadFaceRight,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_WEST => ImGuiKey.GamepadFaceLeft,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_NORTH => ImGuiKey.GamepadFaceUp,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER => ImGuiKey.GamepadL1,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER => ImGuiKey.GamepadR1,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_STICK => ImGuiKey.GamepadL3,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_STICK => ImGuiKey.GamepadR3,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_UP => ImGuiKey.GamepadDpadUp,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_DOWN => ImGuiKey.GamepadDpadDown,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_LEFT => ImGuiKey.GamepadDpadLeft,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_RIGHT => ImGuiKey.GamepadDpadRight,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_START => ImGuiKey.GamepadStart,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_BACK => ImGuiKey.GamepadBack,
            _ => null
        };

    private static ImGuiMouseButton? ConvertMouseButtonEvent(SDL_MouseButtonEvent ev) =>
        ev.Button switch
        {
            SDLButton.SDL_BUTTON_LEFT => ImGuiMouseButton.Left,
            SDLButton.SDL_BUTTON_MIDDLE => ImGuiMouseButton.Middle,
            SDLButton.SDL_BUTTON_RIGHT => ImGuiMouseButton.Right,
            _ => null
        };

    private static void UpdateKeyboardModifiers(SDL_Keymod mod)
    {
        var io = ImGui.GetIO();

        io.AddKeyEvent(ImGuiKey.ModCtrl, (mod & SDL_Keymod.SDL_KMOD_CTRL) != 0);
        io.AddKeyEvent(ImGuiKey.ModAlt, (mod & SDL_Keymod.SDL_KMOD_ALT) != 0);
        io.AddKeyEvent(ImGuiKey.ModShift, (mod & SDL_Keymod.SDL_KMOD_SHIFT) != 0);
        io.AddKeyEvent(ImGuiKey.ModSuper, (mod & SDL_Keymod.SDL_KMOD_GUI) != 0);
    }

    private static ImGuiKey? ConvertKeyboardEventKey(SDL_KeyboardEvent ev)
    {
        var code = ev.scancode switch
        {
            < SDL_Scancode.SDL_SCANCODE_A or >= SDL_Scancode.SDL_NUM_SCANCODES => ImGuiKey.None,
            SDL_Scancode.SDL_SCANCODE_A => ImGuiKey.A,
            SDL_Scancode.SDL_SCANCODE_B => ImGuiKey.B,
            SDL_Scancode.SDL_SCANCODE_C => ImGuiKey.C,
            SDL_Scancode.SDL_SCANCODE_D => ImGuiKey.D,
            SDL_Scancode.SDL_SCANCODE_E => ImGuiKey.E,
            SDL_Scancode.SDL_SCANCODE_F => ImGuiKey.F,
            SDL_Scancode.SDL_SCANCODE_G => ImGuiKey.G,
            SDL_Scancode.SDL_SCANCODE_H => ImGuiKey.H,
            SDL_Scancode.SDL_SCANCODE_I => ImGuiKey.I,
            SDL_Scancode.SDL_SCANCODE_J => ImGuiKey.J,
            SDL_Scancode.SDL_SCANCODE_K => ImGuiKey.K,
            SDL_Scancode.SDL_SCANCODE_L => ImGuiKey.L,
            SDL_Scancode.SDL_SCANCODE_M => ImGuiKey.M,
            SDL_Scancode.SDL_SCANCODE_N => ImGuiKey.N,
            SDL_Scancode.SDL_SCANCODE_O => ImGuiKey.O,
            SDL_Scancode.SDL_SCANCODE_P => ImGuiKey.P,
            SDL_Scancode.SDL_SCANCODE_Q => ImGuiKey.Q,
            SDL_Scancode.SDL_SCANCODE_R => ImGuiKey.R,
            SDL_Scancode.SDL_SCANCODE_S => ImGuiKey.S,
            SDL_Scancode.SDL_SCANCODE_T => ImGuiKey.T,
            SDL_Scancode.SDL_SCANCODE_U => ImGuiKey.U,
            SDL_Scancode.SDL_SCANCODE_V => ImGuiKey.V,
            SDL_Scancode.SDL_SCANCODE_W => ImGuiKey.W,
            SDL_Scancode.SDL_SCANCODE_X => ImGuiKey.X,
            SDL_Scancode.SDL_SCANCODE_Y => ImGuiKey.Y,
            SDL_Scancode.SDL_SCANCODE_Z => ImGuiKey.Z,
            SDL_Scancode.SDL_SCANCODE_1 => ImGuiKey._1,
            SDL_Scancode.SDL_SCANCODE_2 => ImGuiKey._2,
            SDL_Scancode.SDL_SCANCODE_3 => ImGuiKey._3,
            SDL_Scancode.SDL_SCANCODE_4 => ImGuiKey._4,
            SDL_Scancode.SDL_SCANCODE_5 => ImGuiKey._5,
            SDL_Scancode.SDL_SCANCODE_6 => ImGuiKey._6,
            SDL_Scancode.SDL_SCANCODE_7 => ImGuiKey._7,
            SDL_Scancode.SDL_SCANCODE_8 => ImGuiKey._8,
            SDL_Scancode.SDL_SCANCODE_9 => ImGuiKey._9,
            SDL_Scancode.SDL_SCANCODE_0 => ImGuiKey._0,
            SDL_Scancode.SDL_SCANCODE_RETURN => ImGuiKey.Enter,
            SDL_Scancode.SDL_SCANCODE_ESCAPE => ImGuiKey.Escape,
            SDL_Scancode.SDL_SCANCODE_BACKSPACE => ImGuiKey.Backspace,
            SDL_Scancode.SDL_SCANCODE_TAB => ImGuiKey.Tab,
            SDL_Scancode.SDL_SCANCODE_SPACE => ImGuiKey.Space,
            SDL_Scancode.SDL_SCANCODE_MINUS => ImGuiKey.Minus,
            SDL_Scancode.SDL_SCANCODE_EQUALS => ImGuiKey.Equal,
            SDL_Scancode.SDL_SCANCODE_LEFTBRACKET => ImGuiKey.LeftBracket,
            SDL_Scancode.SDL_SCANCODE_RIGHTBRACKET => ImGuiKey.RightBracket,
            SDL_Scancode.SDL_SCANCODE_BACKSLASH => ImGuiKey.Backslash,
            SDL_Scancode.SDL_SCANCODE_NONUSHASH => ImGuiKey.Backslash,
            SDL_Scancode.SDL_SCANCODE_SEMICOLON => ImGuiKey.Semicolon,
            SDL_Scancode.SDL_SCANCODE_APOSTROPHE => ImGuiKey.Apostrophe,
            SDL_Scancode.SDL_SCANCODE_GRAVE => ImGuiKey.GraveAccent,
            SDL_Scancode.SDL_SCANCODE_COMMA => ImGuiKey.Comma,
            SDL_Scancode.SDL_SCANCODE_PERIOD => ImGuiKey.Period,
            SDL_Scancode.SDL_SCANCODE_SLASH => ImGuiKey.Slash,
            SDL_Scancode.SDL_SCANCODE_CAPSLOCK => ImGuiKey.CapsLock,
            SDL_Scancode.SDL_SCANCODE_F1 => ImGuiKey.F1,
            SDL_Scancode.SDL_SCANCODE_F2 => ImGuiKey.F2,
            SDL_Scancode.SDL_SCANCODE_F3 => ImGuiKey.F3,
            SDL_Scancode.SDL_SCANCODE_F4 => ImGuiKey.F4,
            SDL_Scancode.SDL_SCANCODE_F5 => ImGuiKey.F5,
            SDL_Scancode.SDL_SCANCODE_F6 => ImGuiKey.F6,
            SDL_Scancode.SDL_SCANCODE_F7 => ImGuiKey.F7,
            SDL_Scancode.SDL_SCANCODE_F8 => ImGuiKey.F8,
            SDL_Scancode.SDL_SCANCODE_F9 => ImGuiKey.F9,
            SDL_Scancode.SDL_SCANCODE_F10 => ImGuiKey.F10,
            SDL_Scancode.SDL_SCANCODE_F11 => ImGuiKey.F11,
            SDL_Scancode.SDL_SCANCODE_F12 => ImGuiKey.F12,
            SDL_Scancode.SDL_SCANCODE_PRINTSCREEN => ImGuiKey.PrintScreen,
            SDL_Scancode.SDL_SCANCODE_SCROLLLOCK => ImGuiKey.ScrollLock,
            SDL_Scancode.SDL_SCANCODE_PAUSE => ImGuiKey.Pause,
            SDL_Scancode.SDL_SCANCODE_INSERT => ImGuiKey.Insert,
            SDL_Scancode.SDL_SCANCODE_HOME => ImGuiKey.Home,
            SDL_Scancode.SDL_SCANCODE_PAGEUP => ImGuiKey.PageUp,
            SDL_Scancode.SDL_SCANCODE_DELETE => ImGuiKey.Delete,
            SDL_Scancode.SDL_SCANCODE_END => ImGuiKey.End,
            SDL_Scancode.SDL_SCANCODE_PAGEDOWN => ImGuiKey.PageDown,
            SDL_Scancode.SDL_SCANCODE_RIGHT => ImGuiKey.RightArrow,
            SDL_Scancode.SDL_SCANCODE_LEFT => ImGuiKey.LeftArrow,
            SDL_Scancode.SDL_SCANCODE_DOWN => ImGuiKey.DownArrow,
            SDL_Scancode.SDL_SCANCODE_UP => ImGuiKey.UpArrow,
            SDL_Scancode.SDL_SCANCODE_NUMLOCKCLEAR => ImGuiKey.NumLock,
            SDL_Scancode.SDL_SCANCODE_KP_DIVIDE => ImGuiKey.KeypadDivide,
            SDL_Scancode.SDL_SCANCODE_KP_MULTIPLY => ImGuiKey.KeypadMultiply,
            SDL_Scancode.SDL_SCANCODE_KP_MINUS => ImGuiKey.KeypadSubtract,
            SDL_Scancode.SDL_SCANCODE_KP_PLUS => ImGuiKey.KeypadAdd,
            SDL_Scancode.SDL_SCANCODE_KP_ENTER => ImGuiKey.KeypadEnter,
            SDL_Scancode.SDL_SCANCODE_KP_1 => ImGuiKey.Keypad1,
            SDL_Scancode.SDL_SCANCODE_KP_2 => ImGuiKey.Keypad2,
            SDL_Scancode.SDL_SCANCODE_KP_3 => ImGuiKey.Keypad3,
            SDL_Scancode.SDL_SCANCODE_KP_4 => ImGuiKey.Keypad4,
            SDL_Scancode.SDL_SCANCODE_KP_5 => ImGuiKey.Keypad5,
            SDL_Scancode.SDL_SCANCODE_KP_6 => ImGuiKey.Keypad6,
            SDL_Scancode.SDL_SCANCODE_KP_7 => ImGuiKey.Keypad7,
            SDL_Scancode.SDL_SCANCODE_KP_8 => ImGuiKey.Keypad8,
            SDL_Scancode.SDL_SCANCODE_KP_9 => ImGuiKey.Keypad9,
            SDL_Scancode.SDL_SCANCODE_KP_0 => ImGuiKey.Keypad0,
            SDL_Scancode.SDL_SCANCODE_KP_PERIOD => ImGuiKey.KeypadDecimal,
            SDL_Scancode.SDL_SCANCODE_NONUSBACKSLASH => ImGuiKey.GraveAccent,
            SDL_Scancode.SDL_SCANCODE_KP_EQUALS => ImGuiKey.KeypadEqual,
            SDL_Scancode.SDL_SCANCODE_LCTRL => ImGuiKey.LeftCtrl,
            SDL_Scancode.SDL_SCANCODE_LSHIFT => ImGuiKey.LeftShift,
            SDL_Scancode.SDL_SCANCODE_LALT => ImGuiKey.LeftAlt,
            SDL_Scancode.SDL_SCANCODE_LGUI => ImGuiKey.LeftSuper,
            SDL_Scancode.SDL_SCANCODE_RCTRL => ImGuiKey.RightCtrl,
            SDL_Scancode.SDL_SCANCODE_RSHIFT => ImGuiKey.RightShift,
            SDL_Scancode.SDL_SCANCODE_RALT => ImGuiKey.RightAlt,
            SDL_Scancode.SDL_SCANCODE_RGUI => ImGuiKey.RightSuper,
            _ => ImGuiKey.None
        };

        if (code == ImGuiKey.None)
            return null;

        return code;
    }

    private static void CreateFontTexture()
    {
        var io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out var width, out var height);

        var surface = SDL_CreateSurfaceFrom(width, height, SDL_PIXELFORMAT_RGBA32, pixels, width * 4);
        var texture = SDL_CreateTextureFromSurface(_renderer, surface);

        SDL_SetTextureBlendMode(texture, SDL_BlendMode.SDL_BLENDMODE_BLEND)
            .AssertSdlZero();

        SDL_SetTextureScaleMode(texture, SDL_ScaleMode.SDL_SCALEMODE_LINEAR)
            .AssertSdlZero();

        io.Fonts.SetTexID((IntPtr)texture);
    }

    private static void SetMouseCursor(ImGuiMouseCursor cursor)
    {
        var index = (int)cursor;

        if (cursor == ImGuiMouseCursor.None)
        {
            SDL_HideCursor()
                .AssertSdlZero();
            return;
        }

        if (Cursors[index] == null)
        {
            var id = cursor switch
            {
                ImGuiMouseCursor.Arrow => SDL_SystemCursor.SDL_SYSTEM_CURSOR_DEFAULT,
                ImGuiMouseCursor.TextInput => SDL_SystemCursor.SDL_SYSTEM_CURSOR_TEXT,
                ImGuiMouseCursor.ResizeAll => SDL_SystemCursor.SDL_SYSTEM_CURSOR_MOVE,
                ImGuiMouseCursor.ResizeNS => SDL_SystemCursor.SDL_SYSTEM_CURSOR_NS_RESIZE,
                ImGuiMouseCursor.ResizeEW => SDL_SystemCursor.SDL_SYSTEM_CURSOR_EW_RESIZE,
                ImGuiMouseCursor.ResizeNESW => SDL_SystemCursor.SDL_SYSTEM_CURSOR_NESW_RESIZE,
                ImGuiMouseCursor.ResizeNWSE => SDL_SystemCursor.SDL_SYSTEM_CURSOR_NWSE_RESIZE,
                ImGuiMouseCursor.Hand => SDL_SystemCursor.SDL_SYSTEM_CURSOR_POINTER,
                ImGuiMouseCursor.NotAllowed => SDL_SystemCursor.SDL_SYSTEM_CURSOR_NOT_ALLOWED,
                _ => (SDL_SystemCursor?)null
            };

            if (id != null)
            {
                var newCursor = SDL_CreateSystemCursor(id.Value);
                AssertSdl.NotNull(newCursor);
                Cursors[index] = newCursor;
            }
        }

        if (Cursors[index] == null)
        {
            SDL_HideCursor()
                .AssertSdlZero();
        }
        else
        {
            SDL_ShowCursor()
                .AssertSdlZero();

            SDL_SetCursor(Cursors[index])
                .AssertSdlZero();
        }
    }

    private static void Render(SDL_Renderer* renderer, ImDrawDataPtr drawData)
    {
        var io = ImGui.GetIO();

        var displayPos = drawData.DisplayPos;
        var clipOff = new Vector4(displayPos.X, displayPos.Y, displayPos.X, displayPos.Y);

        //
        // Find the maximum number of vertices to allocate for.
        //

        var maxNumVertices = 0;
        for (var n = 0; n < drawData.CmdListsCount; n++)
        {
            var numVertices = drawData.CmdLists[n].VtxBuffer.Size;
            if (maxNumVertices < numVertices)
                maxNumVertices = numVertices;
        }

        var colorBuf = stackalloc Vector4[maxNumVertices];

        //
        // Process command lists.
        //

        for (var n = 0; n < drawData.CmdListsCount; n++)
        {
            var cmdList = drawData.CmdLists[n];
            var vtxBuffer = cmdList.VtxBuffer;
            var idxBuffer = cmdList.IdxBuffer;

            for (var cmdI = 0; cmdI < cmdList.CmdBuffer.Size; cmdI++)
            {
                var pcmd = cmdList.CmdBuffer[cmdI];
                if (pcmd.UserCallback != IntPtr.Zero)
                {
                    Marshal.GetDelegateForFunctionPointer<ImGuiUserCallback>(pcmd.UserCallback)
                        .Invoke(cmdList.NativePtr, pcmd.NativePtr);
                }
                else
                {
                    var bounds = pcmd.ClipRect - clipOff;

                    if (bounds.Z <= bounds.X || bounds.W <= bounds.Y)
                        continue;

                    var clip = new SDL_Rect
                    {
                        x = (int)bounds.X,
                        y = (int)bounds.Y,
                        w = (int)(bounds.Z - bounds.X),
                        h = (int)(bounds.W - bounds.Y)
                    };

                    SDL_SetRenderClipRect(renderer, &clip)
                        .AssertSdlZero();

                    var sdlTexture = (SDL_Texture*)pcmd.TextureId;
                    var idxBufferPtr = (ushort*)idxBuffer.Data + pcmd.IdxOffset;
                    var vtxBufferPtr = (ImDrawVert*)vtxBuffer.Data + pcmd.VtxOffset;

                    for (var i = 0; i < vtxBuffer.Size; i++)
                    {
                        var src = (byte*)&vtxBufferPtr[i].col;
                        colorBuf[i] = new Vector4(src[0], src[1], src[2], src[3]) / 255;
                    }

                    SDL_RenderGeometryRaw(
                            renderer,
                            sdlTexture,
                            (float*)&vtxBufferPtr->pos,
                            sizeof(ImDrawVert),
                            (SDL_FColor*)colorBuf,
                            sizeof(SDL_FColor),
                            (float*)&vtxBufferPtr->uv,
                            sizeof(ImDrawVert),
                            (int)(vtxBuffer.Size - pcmd.VtxOffset),
                            (IntPtr)idxBufferPtr,
                            (int)pcmd.ElemCount,
                            sizeof(ushort))
                        .AssertSdlZero();
                }
            }
        }

        if ((io.ConfigFlags & ImGuiConfigFlags.NoMouseCursorChange) == 0)
            SetMouseCursor(ImGui.GetMouseCursor());
    }
}