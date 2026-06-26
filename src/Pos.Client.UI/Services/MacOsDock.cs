using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Platform;

namespace Pos.Client.UI.Services;

/// <summary>
/// Đặt icon Dock của macOS lúc chạy (khi chạy bằng <c>dotnet run</c> chưa có app bundle .icns nên Dock
/// hiển thị icon .NET mặc định). Gọi native AppKit <c>NSApplication.setApplicationIconImage:</c>.
/// Bản đóng gói thật dùng .icns trong bundle (xem deploy) — hàm này chỉ để dev thấy ngay logo.
/// </summary>
[SupportedOSPlatform("macos")]
public static class MacOsDock
{
    private const string Objc = "/usr/lib/libobjc.dylib";

    [DllImport(Objc, EntryPoint = "objc_getClass")]
    private static extern IntPtr GetClass(string name);

    [DllImport(Objc, EntryPoint = "sel_registerName")]
    private static extern IntPtr Sel(string name);

    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendStr(IntPtr receiver, IntPtr selector,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string arg);

    /// <summary>Đặt icon Dock từ tài nguyên avares <paramref name="assetUri"/> (vd logo.png).</summary>
    public static void SetIcon(string assetUri)
    {
        try
        {
            // NSImage cần đường dẫn file thật → trích tài nguyên nhúng ra file tạm.
            var tmp = Path.Combine(Path.GetTempPath(), "eggs_dock_logo.png");
            using (var src = AssetLoader.Open(new Uri(assetUri)))
            using (var dst = File.Create(tmp))
                src.CopyTo(dst);

            var nsApp = Send(GetClass("NSApplication"), Sel("sharedApplication"));
            if (nsApp == IntPtr.Zero) return;

            var nsString = SendStr(GetClass("NSString"), Sel("stringWithUTF8String:"), tmp);
            var nsImage = SendPtr(Send(GetClass("NSImage"), Sel("alloc")),
                Sel("initWithContentsOfFile:"), nsString);
            if (nsImage == IntPtr.Zero) return;

            SendPtr(nsApp, Sel("setApplicationIconImage:"), nsImage);
        }
        catch
        {
            // Không phải lỗi nghiệp vụ — icon Dock chỉ là trang trí; bỏ qua nếu môi trường không hỗ trợ.
        }
    }
}
