using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace MusicBar.Interop;

/// <summary>
/// Reads the taskbar's controls through Windows' UI Automation COM API, called straight through its
/// vtables. The managed System.Windows.Automation kept native memory from every search it made (about
/// 36 MB after a thousand scans, which MusicBar makes in 40 minutes, and still climbing), and even
/// through COM each string read leaves some 60 bytes behind inside UI Automation. So every interface
/// is released by hand, a scan reads positions only, and the names are read again only when the
/// controls moved (an app opened or closed, a tray icon came or went) or every few minutes.
/// </summary>
internal static unsafe class TaskbarAutomation
{
    /// <summary>One control on the taskbar, its bounds in physical screen pixels.</summary>
    public readonly record struct Control(Native.RECT Bounds, bool Offscreen, string ClassName, string AutomationId, string Name);

    static readonly TimeSpan NamesKeep = TimeSpan.FromMinutes(5);

    // Property ids (UIAutomationClient.h).
    const int BoundingRectangle = 30001, Name = 30005, AutomationId = 30011, ClassName = 30012, IsOffscreen = 30022;

    // Vtable slots, after IUnknown's three.
    const int UiaElementFromHandle = 6, UiaCreateCacheRequest = 20, UiaCreateTrueCondition = 21;
    const int RequestAddProperty = 3, RequestPutElementMode = 11;
    const int ElementFindAllBuildCache = 8, ElementCachedName = 55, ElementCachedAutomationId = 61,
        ElementCachedClassName = 62, ElementCachedIsOffscreen = 70, ElementCachedBoundingRectangle = 75;
    const int ArrayLength = 3, ArrayGetElement = 4;

    [DllImport("ole32.dll")]
    static extern int CoCreateInstance(in Guid clsid, IntPtr outer, int context, in Guid iid, out IntPtr obj);

    static readonly Guid CUIAutomation8 = new("e22ad333-b25f-460c-83d0-0581107395c9");
    static readonly Guid IID_IUIAutomation = new("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee");

    static readonly object Gate = new();
    static IntPtr _uia, _request, _all;

    /// <summary>False once Windows' UI Automation COM object could not be made; a failed scan alone doesn't change it.</summary>
    public static bool Works { get; private set; } = true;

    // The last controls read per taskbar, for their names while the layout holds.
    static readonly Dictionary<IntPtr, (Native.RECT[] Bounds, bool[] Offscreen, Control[] Controls, DateTime NamesAt)> Last = [];

    /// <summary>Every control under the taskbar window, in tree order. Off the UI thread; throws when UI Automation fails.</summary>
    public static List<Control> Read(IntPtr taskbar)
    {
        lock (Gate)
        {
            if (_uia == IntPtr.Zero)
            {
                try
                {
                    Start();
                }
                catch (Exception ex)
                {
                    Works = false;
                    Infrastructure.Log.Error("Windows' UI Automation (COM) isn't available: the managed client reads the taskbar", ex);
                    throw;
                }
            }
            IntPtr root = IntPtr.Zero, found = IntPtr.Zero;
            var elements = new List<IntPtr>();
            try
            {
                Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Slot(_uia, UiaElementFromHandle))(_uia, taskbar, &root));
                Check(((delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr, IntPtr, IntPtr*, int>)Slot(root, ElementFindAllBuildCache))(
                    root, 4 /* TreeScope_Descendants */, _all, _request, &found));
                int n;
                Check(((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Slot(found, ArrayLength))(found, &n));
                var bounds = new Native.RECT[n];
                var offscreen = new bool[n];
                for (int i = 0; i < n; i++)
                {
                    IntPtr e;
                    Check(((delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr*, int>)Slot(found, ArrayGetElement))(found, i, &e));
                    elements.Add(e);
                    int off;
                    Native.RECT r;
                    Check(((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Slot(e, ElementCachedIsOffscreen))(e, &off));
                    Check(((delegate* unmanaged[Stdcall]<IntPtr, Native.RECT*, int>)Slot(e, ElementCachedBoundingRectangle))(e, &r));
                    bounds[i] = r;
                    offscreen[i] = off != 0;
                }

                if (Last.TryGetValue(taskbar, out var last) && DateTime.Now - last.NamesAt < NamesKeep
                    && last.Offscreen.SequenceEqual(offscreen) && last.Bounds.Length == n
                    && Enumerable.Range(0, n).All(i => last.Bounds[i].Same(bounds[i])))
                    return [.. last.Controls];

                var controls = new Control[n];
                for (int i = 0; i < n; i++)
                {
                    var e = elements[i];
                    controls[i] = new Control(bounds[i], offscreen[i],
                        Text(e, ElementCachedClassName), Text(e, ElementCachedAutomationId), Text(e, ElementCachedName));
                }
                // Explorer restarting gives the taskbars new windows: old ones are not kept for ever.
                if (Last.Count > 8) Last.Clear();
                Last[taskbar] = (bounds, offscreen, controls, DateTime.Now);
                return [.. controls];
            }
            finally
            {
                foreach (var e in elements) Release(e);
                Release(found);
                Release(root);
            }
        }
    }

    /// <summary>The automation object, a cache request for the five properties (no live elements kept), and a condition matching all.</summary>
    static void Start()
    {
        Check(CoCreateInstance(CUIAutomation8, IntPtr.Zero, 1 /* CLSCTX_INPROC_SERVER */, IID_IUIAutomation, out var uia));
        IntPtr request = IntPtr.Zero, all = IntPtr.Zero;
        try
        {
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(uia, UiaCreateCacheRequest))(uia, &request));
            foreach (int property in new[] { BoundingRectangle, Name, AutomationId, ClassName, IsOffscreen })
                Check(((delegate* unmanaged[Stdcall]<IntPtr, int, int>)Slot(request, RequestAddProperty))(request, property));
            Check(((delegate* unmanaged[Stdcall]<IntPtr, int, int>)Slot(request, RequestPutElementMode))(request, 0 /* AutomationElementMode_None */));
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(uia, UiaCreateTrueCondition))(uia, &all));
        }
        catch
        {
            Release(all);
            Release(request);
            Release(uia);
            throw;
        }
        (_uia, _request, _all) = (uia, request, all);
    }

    static string Text(IntPtr element, int slot)
    {
        IntPtr text;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(element, slot))(element, &text));
        try
        {
            return text == IntPtr.Zero ? "" : Marshal.PtrToStringBSTR(text);
        }
        finally
        {
            if (text != IntPtr.Zero) Marshal.FreeBSTR(text);
        }
    }

    static void* Slot(IntPtr obj, int slot) => (*(void***)obj)[slot];

    static void Release(IntPtr obj)
    {
        if (obj != IntPtr.Zero) ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Slot(obj, 2))(obj);
    }

    static void Check(int hr)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }
}
