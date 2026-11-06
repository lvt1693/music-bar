// MusicBar.TaskbarTap: makes the Windows 11 taskbar's background see-through
// while MusicBar asks for it, so MusicBar's progress layer shows under the
// taskbar icons.
//
// The Windows 11 taskbar draws its background in XAML inside Explorer; no window
// attribute reaches it. The approach follows TranslucentTB (written independently
// here):
//   1. MusicBar sets a WH_CALLWNDPROC hook on the taskbar's thread, which makes
//      Windows load this DLL into Explorer.
//   2. Inside Explorer, the DLL connects to Explorer's XAML through the public
//      diagnostics API (InitializeXamlDiagnosticsEx, what Visual Studio's Live
//      Visual Tree uses), which keeps it loaded; MusicBar then drops the hook.
//      The taskbar is system XAML (Windows.UI.Xaml) up to at least 26H2; WinUI 3,
//      whose diagnostics entry lives in Microsoft.Internal.FrameworkUdk.dll, is
//      used only where system XAML's entry is missing.
//   3. As a XAML diagnostics "TAP", it watches the visual tree for the taskbar's
//      background rectangles (BackgroundFill and BackgroundStroke inside each
//      TaskbarFrame) and hides them (opacity 0), putting their own opacity back
//      later. Their brushes are never touched: a brush swapped out and put back
//      stops following light and dark mode, and when the taskbar re-adds the
//      rectangles on a light/dark switch, the clear brush was taken for the
//      taskbar's own and "put back", leaving it clear until Explorer restarted.
//      Everything goes through the diagnostics service's generic property calls,
//      so the same code serves both XAML frameworks.
//
// MusicBar names the taskbars it wants clear in a small shared-memory list: only
// those lose their background (a taskbar on another monitor, with nothing of
// MusicBar's behind it, keeps its own). Each TaskbarFrame is matched to its taskbar
// window through the XAML island (DesktopWindowXamlSource) that hosts it. Once the
// list is emptied, or MusicBar exits or crashes (the list then disappears), every
// taskbar gets its own brush back.

#include <windows.h>
#include <ocidl.h>
#include <xamlom.h>
#include <wrl/client.h>
#include <windows.ui.xaml.hosting.desktopwindowxamlsource.h>
// windows.h's GetCurrentTime macro collides with a XAML method of the same name.
#undef GetCurrentTime
#include <windows.ui.xaml.hosting.h>

#include <algorithm>
#include <atomic>
#include <cstdarg>
#include <cstdio>
#include <map>
#include <memory>
#include <mutex>
#include <set>
#include <string>
#include <thread>
#include <vector>

using Microsoft::WRL::ComPtr;

// {391FC26B-9159-4921-8A61-FD6A21601EEC}
static const CLSID CLSID_TaskbarTap = { 0x391fc26b, 0x9159, 0x4921, { 0x8a, 0x61, 0xfd, 0x6a, 0x21, 0x60, 0x1e, 0xec } };

// Written by MusicBar (Interop/XamlTaskbar.cs): the taskbar windows to keep clear.
static const wchar_t TargetsName[] = L"Local\\MusicBar.TaskbarClear.v2";
// Versioned: a newer helper attaches even while an older one is still loaded in this Explorer.
static const wchar_t ReadyPrefix[] = L"Local\\MusicBar.TaskbarTap.Ready.v13.";
// Exists while this helper watches the taskbar's XAML. Explorer lets one helper watch at a
// time, so a helper loaded next to an older one that never steps aside sees nothing.
static const wchar_t WatchingPrefix[] = L"Local\\MusicBar.TaskbarTap.Watching.v13.";
// Set by a newer MusicBar: this helper gives the taskbars back and stops watching, so the
// newer helper can watch without Explorer restarting.
static const wchar_t RetireEvent[] = L"Local\\MusicBar.TaskbarTap.Retire.v13";
// Set while a taskbar background is hidden: MusicBar keeps its layer under the taskbar only when
// the taskbar really is see-through, else the bar would sit hidden behind a solid one.
static const wchar_t ClearedPrefix[] = L"Local\\MusicBar.TaskbarTap.Cleared.v13.";

struct TargetList
{
    UINT32 count;
    // The Windows taskbar mode to put back should MusicBar end without doing it: 0 none, 1 dark, 2 light.
    UINT32 putBack;
    UINT64 windows[16];
};
static const wchar_t PumpClass[] = L"MusicBar.TaskbarTap.Pump";
static const UINT WM_APPLY = WM_APP + 1;

static HMODULE g_module;
// Some connection has shown a taskbar background, so the search for the taskbar's XAML can stop.
static std::atomic<bool> g_sawTaskbar{ false };
// Connections that have walked their XAML tree, so the next is only made once the last has looked.
static std::atomic<int> g_walks{ 0 };

namespace
{
    // A line in %LOCALAPPDATA%\MusicBar\taskbar-helper.log: the helper runs inside Explorer,
    // where nothing else would show what went wrong.
    void Trace(const wchar_t* format, ...)
    {
        wchar_t path[MAX_PATH];
        if (!ExpandEnvironmentStringsW(L"%LOCALAPPDATA%\\MusicBar\\taskbar-helper.log", path, MAX_PATH)) return;
        wchar_t line[512];
        SYSTEMTIME t;
        GetLocalTime(&t);
        int n = swprintf_s(line, L"%04d-%02d-%02d %02d:%02d:%02d.%03d [%lu] ", t.wYear, t.wMonth, t.wDay,
            t.wHour, t.wMinute, t.wSecond, t.wMilliseconds, GetCurrentProcessId());
        va_list args;
        va_start(args, format);
        // Cut short rather than overflow: the secure printf family ends the process otherwise, and this is Explorer.
        _vsnwprintf_s(line + n, _countof(line) - n - 2, _TRUNCATE, format, args);
        va_end(args);
        n += static_cast<int>(wcslen(line + n));
        line[n++] = L'\r';
        line[n++] = L'\n';
        char utf8[1024];
        int bytes = WideCharToMultiByte(CP_UTF8, 0, line, n, utf8, sizeof(utf8), nullptr, nullptr);
        HANDLE file = CreateFileW(path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_ALWAYS, 0, nullptr);
        if (file == INVALID_HANDLE_VALUE) return;
        DWORD written;
        WriteFile(file, utf8, bytes, &written, nullptr);
        CloseHandle(file);
    }

    std::wstring ReadyEventName() { return ReadyPrefix + std::to_wstring(GetCurrentProcessId()); }

    bool RetireRequested()
    {
        HANDLE e = OpenEventW(SYNCHRONIZE, FALSE, RetireEvent);
        if (!e) return false;
        bool set = WaitForSingleObject(e, 0) == WAIT_OBJECT_0;
        CloseHandle(e);
        return set;
    }

    // The taskbars MusicBar wants clear, sorted; empty when MusicBar asks for none or is gone.
    // <present> says whether MusicBar is there at all, <putBack> what it would want put back if not.
    std::vector<HWND> WantedTaskbars(bool& present, UINT32& putBack)
    {
        std::vector<HWND> wanted;
        present = false;
        putBack = 0;
        HANDLE map = OpenFileMappingW(FILE_MAP_READ, FALSE, TargetsName);
        if (!map) return wanted;
        present = true;
        if (auto list = static_cast<const TargetList*>(MapViewOfFile(map, FILE_MAP_READ, 0, 0, sizeof(TargetList))))
        {
            UINT32 n = list->count < 16 ? list->count : 16;
            for (UINT32 i = 0; i < n; i++) wanted.push_back(reinterpret_cast<HWND>(static_cast<UINT_PTR>(list->windows[i])));
            putBack = list->putBack;
            UnmapViewOfFile(list);
        }
        CloseHandle(map);
        std::sort(wanted.begin(), wanted.end());
        return wanted;
    }

    // MusicBar ended without putting Windows' taskbar mode back (ended from Task Manager, or a
    // crash): done here instead, the way Settings does it.
    void PutBackMode(UINT32 code)
    {
        DWORD light = code == 2 ? 1 : 0;
        HKEY key;
        if (RegOpenKeyExW(HKEY_CURRENT_USER, L"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", 0,
                KEY_QUERY_VALUE | KEY_SET_VALUE, &key) != ERROR_SUCCESS) return;
        DWORD now = 0, size = sizeof(now);
        bool differs = RegQueryValueExW(key, L"SystemUsesLightTheme", nullptr, nullptr, reinterpret_cast<BYTE*>(&now), &size) != ERROR_SUCCESS
            || now != light;
        if (differs) RegSetValueExW(key, L"SystemUsesLightTheme", 0, REG_DWORD, reinterpret_cast<const BYTE*>(&light), sizeof(light));
        RegCloseKey(key);
        if (!differs) return;
        SendMessageTimeoutW(HWND_BROADCAST, WM_SETTINGCHANGE, 0, reinterpret_cast<LPARAM>(L"ImmersiveColorSet"), SMTO_ABORTIFHUNG, 1000, nullptr);
        Trace(L"MusicBar ended without putting Windows' mode back: set it to %s", light ? L"light" : L"dark");
    }

    int TaskbarWindows()
    {
        int n = FindWindowW(L"Shell_TrayWnd", nullptr) ? 1 : 0;
        for (HWND h = nullptr; (h = FindWindowExW(nullptr, h, L"Shell_SecondaryTrayWnd", nullptr)) != nullptr;) n++;
        return n;
    }

    bool EndsWith(const std::wstring& s, const wchar_t* tail)
    {
        size_t n = wcslen(tail);
        return s.size() >= n && s.compare(s.size() - n, n, tail) == 0;
    }

    // How a hidden rectangle's own opacity comes back: the local value it had (as text, the way
    // the diagnostics service reads and writes values), or none at all.
    struct Own
    {
        bool local = false;
        std::wstring value;
    };

    // Shared by every connection this helper makes: two of them can reach the same XAML, and only
    // one may handle each rectangle, or the second takes the first one's zero for its own opacity.
    struct Shared
    {
        std::mutex lock;
        std::map<InstanceHandle, const void*> owner;
        // The rectangles hidden now, by element. Kept when the taskbar takes one out of the tree and
        // puts it back (it does on a light/dark switch), so the zero set here is never taken for its own.
        std::map<InstanceHandle, Own> hidden;
    };
    Shared g_shared;

    // One background rectangle. Only touched on its own XAML thread.
    struct Surface
    {
        InstanceHandle element = 0;
        DWORD thread = 0;
        unsigned int opacity = 0;
        std::wstring valueType;
        InstanceHandle zero = 0;
        // The last error hiding it, so a lasting one is logged once rather than every second.
        HRESULT failed = S_OK;
        ComPtr<IInspectable> keepZero;
        // The taskbar window it belongs to, once found.
        HWND taskbar = nullptr;
        bool reportedUnknown = false;
    };
}

class TaskbarTap final : public IObjectWithSite, public IVisualTreeServiceCallback2
{
public:
    // IUnknown
    STDMETHODIMP QueryInterface(REFIID riid, void** out) override
    {
        if (!out) return E_POINTER;
        if (riid == __uuidof(IUnknown) || riid == __uuidof(IObjectWithSite))
            *out = static_cast<IObjectWithSite*>(this);
        else if (riid == __uuidof(IVisualTreeServiceCallback) || riid == __uuidof(IVisualTreeServiceCallback2))
            *out = static_cast<IVisualTreeServiceCallback2*>(this);
        else
        {
            *out = nullptr;
            return E_NOINTERFACE;
        }
        AddRef();
        return S_OK;
    }

    STDMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&_refs); }

    STDMETHODIMP_(ULONG) Release() override
    {
        ULONG refs = InterlockedDecrement(&_refs);
        if (refs == 0) delete this;
        return refs;
    }

    // IObjectWithSite: XAML diagnostics hands over its services here.
    STDMETHODIMP SetSite(IUnknown* site) override
    {
        _site = site;
        if (!site) return S_OK;
        site->QueryInterface(IID_PPV_ARGS(&_diagnostics));
        site->QueryInterface(IID_PPV_ARGS(&_tree));
        Trace(L"SetSite: diagnostics %p, tree service %p", _diagnostics.Get(), _tree.Get());
        if (!_diagnostics || !_tree) return E_NOINTERFACE;

        // Subscribing from inside SetSite hangs; a thread of our own does it (the
        // callbacks still arrive on the XAML thread) and then follows MusicBar's request.
        AddRef();
        HANDLE thread = CreateThread(nullptr, 0, [](void* self) -> DWORD
        {
            static_cast<TaskbarTap*>(self)->Run();
            return 0;
        }, this, 0, nullptr);
        if (thread) CloseHandle(thread);
        else Release();
        return S_OK;
    }

    STDMETHODIMP GetSite(REFIID riid, void** out) override
    {
        if (!_site) return E_FAIL;
        return _site->QueryInterface(riid, out);
    }

    // IVisualTreeServiceCallback: every existing element first, then every change,
    // on the element's XAML thread. The strings in the element belong to us.
    STDMETHODIMP OnVisualTreeChange(ParentChildRelation relation, VisualElement element, VisualMutationType mutation) override
    {
        std::wstring type = element.Type ? element.Type : L"";
        std::wstring name = element.Name ? element.Name : L"";
        SysFreeString(element.Type);
        SysFreeString(element.Name);
        SysFreeString(element.SrcInfo.FileName);
        SysFreeString(element.SrcInfo.Hash);

        std::vector<HWND> wanted;
        {
            std::lock_guard<std::mutex> guard(_lock);
            if (mutation == Remove)
            {
                _nodes.erase(element.Handle);
                _sources.erase(element.Handle);
                if (!_surfaces.erase(element.Handle)) return S_OK;
            }
            else
            {
                _nodes[element.Handle] = Node { relation.Parent, type, name };
                if (EndsWith(type, L"Hosting.DesktopWindowXamlSource")) _sources.insert(element.Handle);
                if (!EndsWith(type, L".Xaml.Shapes.Rectangle") || (name != L"BackgroundFill" && name != L"BackgroundStroke")) return S_OK;
                if (!InsideTaskbarFrame(relation.Parent)) return S_OK;
                wanted = _wanted;
            }
        }
        {
            std::lock_guard<std::mutex> guard(g_shared.lock);
            auto owner = g_shared.owner.find(element.Handle);
            if (mutation == Remove)
            {
                if (owner != g_shared.owner.end() && owner->second == this) g_shared.owner.erase(owner);
                return S_OK;
            }
            // Another connection to the same XAML has it already.
            if (owner != g_shared.owner.end() && owner->second != this) return S_OK;
            g_shared.owner[element.Handle] = this;
        }

        auto surface = std::make_shared<Surface>();
        surface->element = element.Handle;
        surface->thread = GetCurrentThreadId();
        HRESULT hr = FindProperty(element.Handle, L"Opacity", surface->opacity);
        if (FAILED(hr))
        {
            Trace(L"Found taskbar %s but no Opacity property: 0x%08X", name.c_str(), hr);
            return S_OK;
        }
        EnsurePump();
        {
            std::lock_guard<std::mutex> guard(_lock);
            _surfaces[element.Handle] = surface;
        }
        Trace(L"Found taskbar %s (%s): %s", name.c_str(), type.c_str(), Describe(element.Handle).c_str());
        g_sawTaskbar = true;
        if (!wanted.empty())
        {
            int taskbars = -1;
            Apply(*surface, ShouldClear(*surface, wanted, taskbars));
        }
        return S_OK;
    }

    STDMETHODIMP OnElementStateChanged(InstanceHandle, VisualElementState, LPCWSTR) override { return S_OK; }

private:
    struct Node
    {
        InstanceHandle parent;
        std::wstring type;
        std::wstring name;
    };

    ~TaskbarTap()
    {
        if (_ready) CloseHandle(_ready);
        if (_watching) CloseHandle(_watching);
    }

    // The diagnostics index of a property, looked up in the element's property list
    // (GetPropertyIndex rejects plain names on some builds).
    HRESULT FindProperty(InstanceHandle element, const wchar_t* property, unsigned int& index)
    {
        HRESULT hr = _tree->GetPropertyIndex(element, property, &index);
        if (SUCCEEDED(hr)) return hr;

        bool found = false;
        hr = ForEachValue(element, [&](const PropertyChainValue& value, const PropertyChainSource*)
        {
            if (found || !value.PropertyName || wcscmp(value.PropertyName, property) != 0) return;
            index = value.Index;
            found = true;
        });
        if (FAILED(hr)) return hr;
        return found ? S_OK : HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
    }

    // XAML thread: what the rectangle's opacity is made of, so hiding it can be undone exactly.
    Own ReadOwnOpacity(Surface& s)
    {
        Own own;
        ForEachValue(s.element, [&](const PropertyChainValue& value, const PropertyChainSource* source)
        {
            if (!value.PropertyName || wcscmp(value.PropertyName, L"Opacity") != 0) return;
            if (s.valueType.empty() && value.ValueType) s.valueType = value.ValueType;
            if (own.local || !source || source->Source != BaseValueSourceLocal) return;
            // A zero is never the taskbar's own but a helper's leftover: putting it back would keep the background hidden.
            if (!value.Value || wcstod(value.Value, nullptr) == 0) return;
            own.local = true;
            own.value = value.Value ? value.Value : L"";
        });
        if (s.valueType.empty()) s.valueType = L"Windows.Foundation.Double";
        return own;
    }

    // For the log: the effective Fill and Opacity, where each comes from (BaseValueSource), and the brush's color.
    std::wstring Describe(InstanceHandle element)
    {
        std::wstring text;
        InstanceHandle brush = 0;
        ForEachValue(element, [&](const PropertyChainValue& value, const PropertyChainSource* source)
        {
            if (value.Overridden || !value.PropertyName) return;
            bool fill = wcscmp(value.PropertyName, L"Fill") == 0;
            if (!fill && wcscmp(value.PropertyName, L"Opacity") != 0) return;
            if (fill && (value.MetadataBits & IsValueHandle) && value.Value) brush = _wcstoui64(value.Value, nullptr, 10);
            wchar_t part[200];
            _snwprintf_s(part, _countof(part), _TRUNCATE, L"%s%s=%.60s (%.60s, source %d)", text.empty() ? L"" : L", ",
                value.PropertyName, value.Value ? value.Value : L"", value.ValueType ? value.ValueType : L"",
                source ? static_cast<int>(source->Source) : -1);
            text += part;
        });
        if (brush)
        {
            ForEachValue(brush, [&](const PropertyChainValue& value, const PropertyChainSource*)
            {
                if (value.Overridden || !value.PropertyName || wcscmp(value.PropertyName, L"Color") != 0) return;
                text += L", brush color ";
                text += value.Value ? value.Value : L"?";
            });
        }
        return text;
    }

    HRESULT Create(const std::wstring& type, const std::wstring& value, InstanceHandle& out)
    {
        BSTR typeName = SysAllocString(type.c_str());
        BSTR text = SysAllocString(value.c_str());
        HRESULT hr = _tree->CreateInstance(typeName, text, &out);
        SysFreeString(typeName);
        SysFreeString(text);
        if (FAILED(hr))
        {
            Trace(L"Creating %s %s failed: 0x%08X", type.c_str(), value.c_str(), hr);
            out = 0;
        }
        return hr;
    }

    // Every value in the element's property chain, with the source it comes from.
    template <typename Visit>
    HRESULT ForEachValue(InstanceHandle element, Visit visit)
    {
        unsigned int sourceCount = 0, valueCount = 0;
        PropertyChainSource* sources = nullptr;
        PropertyChainValue* values = nullptr;
        HRESULT hr = _tree->GetPropertyValuesChain(element, &sourceCount, &sources, &valueCount, &values);
        if (FAILED(hr)) return hr;
        for (unsigned int i = 0; i < valueCount; i++)
            visit(values[i], values[i].PropertyChainIndex < sourceCount ? &sources[values[i].PropertyChainIndex] : nullptr);
        for (unsigned int i = 0; i < valueCount; i++)
        {
            SysFreeString(values[i].Type);
            SysFreeString(values[i].DeclaringType);
            SysFreeString(values[i].ValueType);
            SysFreeString(values[i].ItemType);
            SysFreeString(values[i].Value);
            SysFreeString(values[i].PropertyName);
        }
        for (unsigned int i = 0; i < sourceCount; i++)
        {
            SysFreeString(sources[i].TargetType);
            SysFreeString(sources[i].Name);
            SysFreeString(sources[i].SrcInfo.FileName);
            SysFreeString(sources[i].SrcInfo.Hash);
        }
        CoTaskMemFree(values);
        CoTaskMemFree(sources);
        return S_OK;
    }

    static bool IsFrame(const Node& node)
    {
        return EndsWith(node.type, L"Taskbar.TaskbarFrame") || node.name == L"TaskbarFrame";
    }

    bool InsideTaskbarFrame(InstanceHandle handle) const
    {
        for (int depth = 0; depth < 16 && handle; depth++)
        {
            auto node = _nodes.find(handle);
            if (node == _nodes.end()) return false;
            if (IsFrame(node->second)) return true;
            handle = node->second.parent;
        }
        return false;
    }

    // XAML thread: the taskbar window a background rectangle belongs to. Above its
    // TaskbarFrame sits the XAML island (DesktopWindowXamlSource) whose window lives in
    // the taskbar's; when the tree doesn't link them, the island whose Content is the
    // frame's parent is the one (TranslucentTB's way).
    HWND ResolveTaskbar(InstanceHandle rectangle)
    {
        InstanceHandle frameParent = 0, source = 0;
        std::vector<InstanceHandle> sources;
        {
            std::lock_guard<std::mutex> guard(_lock);
            auto start = _nodes.find(rectangle);
            InstanceHandle h = start == _nodes.end() ? 0 : start->second.parent;
            bool aboveFrame = false;
            for (int depth = 0; depth < 32 && h; depth++)
            {
                auto node = _nodes.find(h);
                if (node == _nodes.end()) break;
                if (!aboveFrame && IsFrame(node->second))
                {
                    aboveFrame = true;
                    frameParent = node->second.parent;
                }
                else if (aboveFrame && EndsWith(node->second.type, L"Hosting.DesktopWindowXamlSource"))
                {
                    source = h;
                    break;
                }
                h = node->second.parent;
            }
            sources.assign(_sources.begin(), _sources.end());
        }
        if (source) return WindowOf(source);
        if (!frameParent) return nullptr;

        ComPtr<IInspectable> root;
        ComPtr<IUnknown> rootId;
        if (FAILED(_diagnostics->GetIInspectableFromHandle(frameParent, &root)) || FAILED(root.As(&rootId))) return nullptr;
        for (InstanceHandle s : sources)
        {
            ComPtr<IInspectable> item;
            ComPtr<ABI::Windows::UI::Xaml::Hosting::IDesktopWindowXamlSource> xamlSource;
            if (FAILED(_diagnostics->GetIInspectableFromHandle(s, &item)) || FAILED(item.As(&xamlSource))) continue;
            // An island of another thread answers RPC_E_WRONG_THREAD: not this one.
            ComPtr<ABI::Windows::UI::Xaml::IUIElement> content;
            ComPtr<IUnknown> contentId;
            if (FAILED(xamlSource->get_Content(&content)) || !content || FAILED(content.As(&contentId))) continue;
            if (contentId.Get() == rootId.Get()) return WindowOf(s);
        }
        return nullptr;
    }

    // The top-level window (Shell_TrayWnd or Shell_SecondaryTrayWnd) holding a XAML island.
    HWND WindowOf(InstanceHandle source)
    {
        ComPtr<IInspectable> item;
        ComPtr<IDesktopWindowXamlSourceNative> native;
        if (FAILED(_diagnostics->GetIInspectableFromHandle(source, &item)) || FAILED(item.As(&native))) return nullptr;
        HWND island = nullptr;
        if (FAILED(native->get_WindowHandle(&island)) || !island) return nullptr;
        return GetAncestor(island, GA_ROOT);
    }

    // XAML thread: whether a surface should be clear. A rectangle whose taskbar can't be
    // found is only cleared when there is a single taskbar, so it can't be another monitor's.
    bool ShouldClear(Surface& s, const std::vector<HWND>& wanted, int& taskbars)
    {
        if (wanted.empty()) return false;
        if (!s.taskbar)
        {
            s.taskbar = ResolveTaskbar(s.element);
            if (s.taskbar)
            {
                wchar_t cls[64] = L"";
                GetClassNameW(s.taskbar, cls, 64);
                Trace(L"Taskbar background %llu belongs to window %p (%s)", static_cast<unsigned long long>(s.element), s.taskbar, cls);
            }
            else if (!s.reportedUnknown)
            {
                s.reportedUnknown = true;
                Trace(L"Taskbar background %llu: window not found yet", static_cast<unsigned long long>(s.element));
            }
        }
        if (s.taskbar) return std::binary_search(wanted.begin(), wanted.end(), s.taskbar);
        if (taskbars < 0) taskbars = TaskbarWindows();
        return taskbars == 1;
    }

    // XAML thread: hides the rectangle, or gives it its own opacity back. Its brush stays the
    // taskbar's, so it keeps up with light and dark mode while hidden.
    void Apply(Surface& s, bool clear)
    {
        bool hidden;
        Own own;
        {
            std::lock_guard<std::mutex> guard(g_shared.lock);
            auto found = g_shared.hidden.find(s.element);
            hidden = found != g_shared.hidden.end();
            if (hidden) own = found->second;
        }
        if (!clear)
        {
            if (!hidden) return;
            InstanceHandle value = 0;
            HRESULT hr = !own.local ? _tree->ClearProperty(s.element, s.opacity)
                : SUCCEEDED(Create(s.valueType, own.value, value)) ? _tree->SetProperty(s.element, value, s.opacity)
                : E_FAIL;
            // The taskbar's own value can fail to come back from text: taking ours away shows it as well.
            if (FAILED(hr) && own.local) hr = _tree->ClearProperty(s.element, s.opacity);
            if (FAILED(hr) && hr != HRESULT_FROM_WIN32(ERROR_NOT_FOUND))
            {
                // Still hidden: the watcher tries again every second while MusicBar is away, rather
                // than leaving the taskbar see-through until Explorer restarts.
                if (hr != s.failed) Trace(L"Giving the taskbar its opacity back failed: 0x%08X, trying again", hr);
                s.failed = hr;
                return;
            }
            s.failed = S_OK;
            {
                std::lock_guard<std::mutex> guard(g_shared.lock);
                g_shared.hidden.erase(s.element);
            }
            if (FAILED(hr)) Trace(L"Taskbar background %llu went away while hidden", static_cast<unsigned long long>(s.element));
            else Trace(L"Taskbar background %llu back: %s", static_cast<unsigned long long>(s.element), Describe(s.element).c_str());
            return;
        }
        if (!hidden) own = ReadOwnOpacity(s);
        else if (s.valueType.empty()) ReadOwnOpacity(s);
        if (!s.zero)
        {
            if (FAILED(Create(s.valueType, L"0", s.zero))) return;
            _diagnostics->GetIInspectableFromHandle(s.zero, &s.keepZero);
        }
        // Repeated every second while clear, in case Explorer sets an opacity of its own meanwhile.
        HRESULT hr = _tree->SetProperty(s.element, s.zero, s.opacity);
        if (hr == HRESULT_FROM_WIN32(ERROR_NOT_FOUND))
        {
            // Not found is most often the zero, not the background: XAML lets go of a value once it
            // has been cleared off the property, so after the first time the taskbar got its
            // opacity back, the old zero is unknown. Taken for a background gone away, it left the
            // taskbar never cleared again (and the bar under it out of sight). A new zero, then.
            s.zero = 0;
            s.keepZero.Reset();
            if (SUCCEEDED(Create(s.valueType, L"0", s.zero)))
            {
                _diagnostics->GetIInspectableFromHandle(s.zero, &s.keepZero);
                hr = _tree->SetProperty(s.element, s.zero, s.opacity);
            }
        }
        // Still not found: gone only if the element can't be read any more either.
        if (hr == HRESULT_FROM_WIN32(ERROR_NOT_FOUND) && SUCCEEDED(ForEachValue(s.element, [](const PropertyChainValue&, const PropertyChainSource*) {})))
        {
            if (hr != s.failed) Trace(L"Taskbar background %llu is there but won't take a zero: trying again", static_cast<unsigned long long>(s.element));
            s.failed = hr;
            return;
        }
        if (hr == HRESULT_FROM_WIN32(ERROR_NOT_FOUND))
        {
            // The taskbar built a new background (unannounced when the tree can't be watched).
            Trace(L"Taskbar background %llu is gone", static_cast<unsigned long long>(s.element));
            {
                std::lock_guard<std::mutex> guard(_lock);
                _surfaces.erase(s.element);
                _lost = true;
            }
            std::lock_guard<std::mutex> guard(g_shared.lock);
            g_shared.hidden.erase(s.element);
            g_shared.owner.erase(s.element);
            return;
        }
        if (FAILED(hr))
        {
            if (hr != s.failed) Trace(L"Setting Opacity failed: 0x%08X", hr);
            s.failed = hr;
            return;
        }
        if (hidden) return;
        {
            std::lock_guard<std::mutex> guard(g_shared.lock);
            g_shared.hidden[s.element] = own;
        }
        Trace(L"Hid taskbar background %llu: %s", static_cast<unsigned long long>(s.element), Describe(s.element).c_str());
    }

    // XAML thread: applies MusicBar's request to the surfaces living on this thread.
    void ApplyHere()
    {
        DWORD me = GetCurrentThreadId();
        std::vector<std::shared_ptr<Surface>> mine;
        std::vector<HWND> wanted;
        {
            std::lock_guard<std::mutex> guard(_lock);
            for (auto& entry : _surfaces)
                if (entry.second->thread == me) mine.push_back(entry.second);
            wanted = _wanted;
        }
        int taskbars = -1;
        for (auto& s : mine) Apply(*s, ShouldClear(*s, wanted, taskbars));
    }

    // A message-only window on each XAML thread, so the watcher can hand work to that thread.
    void EnsurePump()
    {
        DWORD me = GetCurrentThreadId();
        {
            std::lock_guard<std::mutex> guard(_lock);
            if (_pumps.count(me)) return;
        }
        static ATOM atom = []
        {
            WNDCLASSW wc = {};
            wc.lpfnWndProc = [](HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam) -> LRESULT
            {
                if (msg == WM_APPLY)
                {
                    auto self = reinterpret_cast<TaskbarTap*>(GetWindowLongPtrW(hwnd, GWLP_USERDATA));
                    if (self) self->ApplyHere();
                    return 0;
                }
                return DefWindowProcW(hwnd, msg, wParam, lParam);
            };
            wc.hInstance = g_module;
            wc.lpszClassName = PumpClass;
            return RegisterClassW(&wc);
        }();
        if (!atom) return;
        HWND pump = CreateWindowExW(0, PumpClass, L"", 0, 0, 0, 0, 0, HWND_MESSAGE, nullptr, g_module, nullptr);
        if (!pump) return;
        SetWindowLongPtrW(pump, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(this));
        std::lock_guard<std::mutex> guard(_lock);
        _pumps[me] = pump;
    }

    void Run()
    {
        HRESULT hr = _tree->AdviseVisualTreeChange(this);
        Trace(L"AdviseVisualTreeChange: 0x%08X", hr);
        g_walks++;
        // Tells MusicBar the helper is in place (and that this Explorer needs no second one),
        // and whether it actually sees the taskbar.
        _ready = CreateEventW(nullptr, TRUE, TRUE, ReadyEventName().c_str());
        // Taking over from an earlier helper, the tree is walked and then the call fails
        // (E_UNEXPECTED): the backgrounds found on the way are still there to work with, but
        // changes are not announced, so a background the taskbar replaces is looked for again.
        bool blind = FAILED(hr);
        if (blind)
        {
            size_t found;
            {
                std::lock_guard<std::mutex> guard(_lock);
                found = _surfaces.size();
            }
            if (!found) return;
            Trace(L"Going on with the %zu taskbar backgrounds found", found);
        }
        _watching = CreateEventW(nullptr, TRUE, TRUE, (WatchingPrefix + std::to_wstring(GetCurrentProcessId())).c_str());
        _cleared = CreateEventW(nullptr, TRUE, FALSE, (ClearedPrefix + std::to_wstring(GetCurrentProcessId())).c_str());

        for (int tick = 0;; tick++)
        {
            if (RetireRequested())
            {
                Retire();
                return;
            }
            bool lost;
            size_t known;
            {
                std::lock_guard<std::mutex> guard(_lock);
                lost = _lost;
                _lost = false;
                known = _surfaces.size();
            }
            // Once when one goes, then every 5 seconds while none is known.
            if (blind && (lost || (!known && tick % 20 == 0)))
            {
                hr = _tree->AdviseVisualTreeChange(this);
                blind = FAILED(hr);
                size_t found;
                {
                    std::lock_guard<std::mutex> guard(_lock);
                    found = _surfaces.size();
                }
                Trace(L"Looked for the taskbar background again: 0x%08X, %zu found", hr, found);
            }
            bool present;
            UINT32 putBack;
            std::vector<HWND> wanted = WantedTaskbars(present, putBack);
            if (present) _putBack = putBack;
            else if (_putBack)
            {
                PutBackMode(_putBack);
                _putBack = 0;
            }
            bool pending;
            {
                std::lock_guard<std::mutex> guard(g_shared.lock);
                pending = !g_shared.hidden.empty();
            }
            if (_cleared)
            {
                if (pending) SetEvent(_cleared);
                else ResetEvent(_cleared);
            }
            std::vector<HWND> pumps;
            bool changed;
            size_t count;
            {
                std::lock_guard<std::mutex> guard(_lock);
                changed = wanted != _wanted;
                _wanted = wanted;
                count = _surfaces.size();
                // While clear, look again every second, in case Explorer sets an opacity of its own;
                // and while a background that should be back is still hidden, try it again.
                if (changed || ((!wanted.empty() || pending) && tick % 4 == 0))
                    for (auto& entry : _pumps) pumps.push_back(entry.second);
            }
            for (HWND pump : pumps) PostMessageW(pump, WM_APPLY, 0, 0);
            if (changed) Trace(L"MusicBar wants %zu taskbar(s) clear (%zu surfaces known)", wanted.size(), count);
            Sleep(250);
        }
    }

    // A newer helper takes over: every taskbar gets its own background back and this one
    // stops watching, which frees the XAML tree for the newcomer.
    void Retire()
    {
        std::vector<HWND> pumps;
        {
            std::lock_guard<std::mutex> guard(_lock);
            _wanted.clear();
            for (auto& entry : _pumps) pumps.push_back(entry.second);
        }
        for (HWND pump : pumps) SendMessageTimeoutW(pump, WM_APPLY, 0, 0, SMTO_ABORTIFHUNG, 2000, nullptr);
        {
            std::lock_guard<std::mutex> guard(g_shared.lock);
            for (auto it = g_shared.owner.begin(); it != g_shared.owner.end();)
                it = it->second == this ? g_shared.owner.erase(it) : std::next(it);
        }
        HRESULT hr = _tree->UnadviseVisualTreeChange(this);
        if (_watching) CloseHandle(_watching);
        _watching = nullptr;
        if (_cleared) CloseHandle(_cleared);
        _cleared = nullptr;
        Trace(L"Retired for a newer helper (unadvise 0x%08X)", hr);
    }

    LONG _refs = 1;
    ComPtr<IUnknown> _site;
    ComPtr<IXamlDiagnostics> _diagnostics;
    ComPtr<IVisualTreeService3> _tree;
    HANDLE _ready = nullptr;
    HANDLE _watching = nullptr;
    HANDLE _cleared = nullptr;

    std::mutex _lock;
    std::map<InstanceHandle, Node> _nodes;
    std::map<InstanceHandle, std::shared_ptr<Surface>> _surfaces;
    std::set<InstanceHandle> _sources;
    std::map<DWORD, HWND> _pumps;
    std::vector<HWND> _wanted;
    // A background went away while the tree couldn't be watched: walk it again to find the new one.
    bool _lost = false;
    // What MusicBar last asked to have put back if it ends (watcher thread only).
    UINT32 _putBack = 0;
};

class Factory final : public IClassFactory
{
public:
    STDMETHODIMP QueryInterface(REFIID riid, void** out) override
    {
        if (!out) return E_POINTER;
        if (riid == __uuidof(IUnknown) || riid == __uuidof(IClassFactory))
        {
            *out = static_cast<IClassFactory*>(this);
            return S_OK;
        }
        *out = nullptr;
        return E_NOINTERFACE;
    }

    STDMETHODIMP_(ULONG) AddRef() override { return 2; }
    STDMETHODIMP_(ULONG) Release() override { return 1; }

    STDMETHODIMP CreateInstance(IUnknown* outer, REFIID riid, void** out) override
    {
        if (outer) return CLASS_E_NOAGGREGATION;
        auto tap = new (std::nothrow) TaskbarTap();
        if (!tap) return E_OUTOFMEMORY;
        HRESULT hr = tap->QueryInterface(riid, out);
        tap->Release();
        return hr;
    }

    STDMETHODIMP LockServer(BOOL) override { return S_OK; }
};

static Factory g_factory;

STDAPI DllGetClassObject(REFCLSID clsid, REFIID riid, void** out)
{
    if (clsid != CLSID_TaskbarTap) return CLASS_E_CLASSNOTAVAILABLE;
    return g_factory.QueryInterface(riid, out);
}

// Explorer keeps the TAP until it exits: the watcher thread lives in this DLL.
STDAPI DllCanUnloadNow() { return S_FALSE; }

// The hook MusicBar sets on the taskbar's thread; it only exists to get this DLL loaded there.
extern "C" LRESULT CALLBACK TapHookProc(int code, WPARAM wParam, LPARAM lParam)
{
    return CallNextHookEx(nullptr, code, wParam, lParam);
}

using InitFn = HRESULT(WINAPI*)(LPCWSTR, DWORD, LPCWSTR, LPCWSTR, CLSID, LPCWSTR);

// XAML names each of its instances in a process "VisualDiagConnection<n>", and the
// connection has to name an existing one; "not found" means try the next number
// (Windhawk's taskbar mods do the same). Starts at <number> and leaves it on the one
// connected to. A successful connection pins this DLL.
static HRESULT Connect(InitFn init, const wchar_t* path, int& number)
{
    HRESULT hr = HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
    for (int last = number + 100; number <= last; number++)
    {
        wchar_t connection[64];
        swprintf_s(connection, L"VisualDiagConnection%d", number);
        hr = init(connection, GetCurrentProcessId(), L"", path, CLSID_TaskbarTap, nullptr);
        if (hr != HRESULT_FROM_WIN32(ERROR_NOT_FOUND)) break;
    }
    return hr;
}

// Runs in Explorer. The taskbar is system XAML (Windows.UI.Xaml) up to at least 26H2;
// WinUI 3, whose entry point lives in Microsoft.Internal.FrameworkUdk.dll, is used only
// where system XAML's is missing: here it reaches the very same instances, and two
// connections watching one instance crashed Explorer. XAML diagnostics initialises once
// per thread, so each try runs on a fresh thread. Explorer runs more than one XAML
// instance, and right after it starts the taskbar's may not exist yet (the first one
// found was once some other part of Explorer, and the taskbar was never seen): the
// instances are connected to one at a time as they appear, each walked before the next,
// until one of them shows a taskbar background.
static DWORD WINAPI Install(void*)
{
    wchar_t path[MAX_PATH];
    if (!GetModuleFileNameW(g_module, path, MAX_PATH)) return 1;
    Trace(L"Loaded into Explorer: %s", path);

    const wchar_t* name = L"system XAML";
    InitFn init = nullptr;
    if (HMODULE wux = LoadLibraryExW(L"Windows.UI.Xaml.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32))
        init = reinterpret_cast<InitFn>(GetProcAddress(wux, "InitializeXamlDiagnosticsEx"));
    if (!init)
    {
        name = L"WinUI 3";
        if (HMODULE udk = GetModuleHandleW(L"Microsoft.Internal.FrameworkUdk.dll"))
            init = reinterpret_cast<InitFn>(GetProcAddress(udk, "InitializeXamlDiagnosticsEx"));
    }
    if (!init)
    {
        Trace(L"No XAML diagnostics in this Explorer");
        return 1;
    }

    int next = 1;
    bool connected = false;
    for (int round = 1; round <= 120 && !g_sawTaskbar; round++)
    {
        while (!g_sawTaskbar)
        {
            HRESULT hr = E_FAIL;
            int number = next;
            int walks = g_walks;
            std::thread([&] { hr = Connect(init, path, number); }).join();
            if (hr == HRESULT_FROM_WIN32(ERROR_NOT_FOUND))
            {
                if (round == 1 && !connected) Trace(L"%s: nothing to connect to yet", name);
                break;
            }
            next = number + 1;
            if (FAILED(hr))
            {
                Trace(L"%s, VisualDiagConnection%d: 0x%08X", name, number, hr);
                continue;
            }
            connected = true;
            Trace(L"Connected to %s as VisualDiagConnection%d", name, number);
            // Let it walk its tree before trying the next one.
            for (int wait = 0; wait < 50 && g_walks == walks && !g_sawTaskbar; wait++) Sleep(100);
        }
        if (!g_sawTaskbar) Sleep(1000);
    }
    if (!g_sawTaskbar) Trace(L"No taskbar background in any XAML instance after two minutes");
    return connected ? 0 : 1;
}

static bool InExplorer()
{
    wchar_t image[MAX_PATH];
    DWORD n = GetModuleFileNameW(nullptr, image, MAX_PATH);
    const wchar_t* file = wcsrchr(image, L'\\');
    return n && file && _wcsicmp(file + 1, L"explorer.exe") == 0;
}

BOOL WINAPI DllMain(HMODULE module, DWORD reason, void*)
{
    if (reason != DLL_PROCESS_ATTACH) return TRUE;
    g_module = module;
    DisableThreadLibraryCalls(module);
    // MusicBar itself also loads the DLL (to set the hook); there it does nothing.
    if (!InExplorer()) return TRUE;

    // Already connected in this Explorer (an earlier hook): nothing more to do.
    HANDLE ready = OpenEventW(SYNCHRONIZE, FALSE, ReadyEventName().c_str());
    if (ready)
    {
        CloseHandle(ready);
        return TRUE;
    }
    static LONG installing = 0;
    if (InterlockedExchange(&installing, 1) != 0) return TRUE;

    // Pinned: once connected, the watcher thread must never run on unloaded code.
    HMODULE pinned;
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
        reinterpret_cast<LPCWSTR>(&DllMain), &pinned);
    HANDLE thread = CreateThread(nullptr, 0, Install, nullptr, 0, nullptr);
    if (thread) CloseHandle(thread);
    return TRUE;
}
