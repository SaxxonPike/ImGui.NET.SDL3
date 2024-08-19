using static SDL.SDL3;
namespace ImGuiNET.SDL3;

internal static class AssertSdl
{
    public static unsafe T* NotNull<T>(T* val) where T : unmanaged =>
        val != null
            ? val
            : throw new Exception(SDL_GetError());
    
    public static int NotZero(int val) =>
        val != 0
            ? val
            : throw new Exception(SDL_GetError());

    public static int AssertSdlNotZero(this int val) =>
        NotZero(val);

    public static IntPtr NotZero(IntPtr val) =>
        val != 0
            ? val
            : throw new Exception(SDL_GetError());

    public static IntPtr AssertSdlNotZero(this IntPtr val) =>
        NotZero(val);

    public static int Zero(int val) =>
        val == 0
            ? val
            : throw new Exception(SDL_GetError());

    public static int AssertSdlZero(this int val) =>
        Zero(val);
    
}