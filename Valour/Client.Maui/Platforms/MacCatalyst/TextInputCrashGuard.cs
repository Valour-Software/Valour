using System.Runtime.InteropServices;
using ObjCRuntime;

namespace Valour.Client.Maui;

/// <summary>
/// Works around a crash in macOS text input for Mac Catalyst apps. The private
/// RemoteTextInput framework keeps each text input session's state in a
/// non-atomic property that its XPC thread writes while the main thread reads
/// and copies it, so the main thread can copy a state object that is being
/// freed (EXC_BAD_ACCESS in -[RTIDocumentState copyWithZone:] under
/// NSTextInputContext inputSessionDidBegin:). This replaces the property's
/// getter and setter with ones that hold a lock, and the getter returns its
/// own reference so the value outlives a concurrent write.
/// See https://steipete.me/posts/2020/mac-catalyst-crash-hunt.
/// </summary>
internal static class TextInputCrashGuard
{
    private const string LibObjC = "/usr/lib/libobjc.dylib";
    private const string FrameworkPath = "/System/Library/PrivateFrameworks/RemoteTextInput.framework/RemoteTextInput";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr Getter(IntPtr self, IntPtr selector);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Setter(IntPtr self, IntPtr selector, IntPtr value);

    private static readonly object Lock = new();

    // Native code calls these, so they must stay reachable for the process lifetime.
    private static readonly Getter LockedGetter = GetDocumentState;
    private static readonly Setter LockedSetter = SetDocumentState;

    private static Getter? _originalGetter;
    private static Setter? _originalSetter;
    private static bool _installed;

    public static void Install()
    {
        if (_installed)
            return;
        _installed = true;

        try
        {
            // The framework may not be loaded until the first text field is used.
            Dlfcn.dlopen(FrameworkPath, 0);

            var sessionClass = Class.GetHandle("RTIInputSystemServiceSession");
            if (sessionClass == IntPtr.Zero)
                return;

            var getMethod = class_getInstanceMethod(sessionClass, Selector.GetHandle("documentState"));
            var setMethod = class_getInstanceMethod(sessionClass, Selector.GetHandle("setDocumentState:"));
            if (getMethod == IntPtr.Zero || setMethod == IntPtr.Zero)
                return;

            _originalGetter = Marshal.GetDelegateForFunctionPointer<Getter>(
                method_setImplementation(getMethod, Marshal.GetFunctionPointerForDelegate(LockedGetter)));
            _originalSetter = Marshal.GetDelegateForFunctionPointer<Setter>(
                method_setImplementation(setMethod, Marshal.GetFunctionPointerForDelegate(LockedSetter)));
        }
        catch (Exception)
        {
            // Without the guard the app runs as before.
        }
    }

    private static IntPtr GetDocumentState(IntPtr self, IntPtr selector)
    {
        IntPtr value;
        lock (Lock)
        {
            value = _originalGetter!(self, selector);
            objc_retain(value);
        }
        return objc_autorelease(value);
    }

    private static void SetDocumentState(IntPtr self, IntPtr selector, IntPtr value)
    {
        lock (Lock)
            _originalSetter!(self, selector, value);
    }

    [DllImport(LibObjC)]
    private static extern IntPtr class_getInstanceMethod(IntPtr cls, IntPtr selector);

    [DllImport(LibObjC)]
    private static extern IntPtr method_setImplementation(IntPtr method, IntPtr implementation);

    [DllImport(LibObjC)]
    private static extern IntPtr objc_retain(IntPtr value);

    [DllImport(LibObjC)]
    private static extern IntPtr objc_autorelease(IntPtr value);
}
