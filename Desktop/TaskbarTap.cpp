#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <unknwn.h>
#include <ocidl.h>
#include <oleauto.h>
#include <stdio.h>
#include <xamlOM.h>

#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "oleaut32.lib")
#pragma comment(lib, "user32.lib")

// {8F9D4A62-3E1B-4C7A-9D2E-5B6A8C1F0E34}
static const CLSID CLSID_TaskbarTap =
{ 0x8f9d4a62, 0x3e1b, 0x4c7a, { 0x9d, 0x2e, 0x5b, 0x6a, 0x8c, 0x1f, 0x0e, 0x34 } };

static HANDLE g_activeMarker = NULL;

static void MarkTapActiveInProcess()
{
    if (g_activeMarker != NULL) return;
    wchar_t name[128];
    wsprintfW(name, L"Local\\LivelyMockTapActive_%u", (unsigned int)GetCurrentProcessId());
    g_activeMarker = CreateEventW(NULL, TRUE, TRUE, name);
}

static bool IsClearWanted()
{
    HANDLE hEv = OpenEventW(SYNCHRONIZE, FALSE, L"Local\\LivelyMockTaskbarClear");
    if (!hEv) return false;
    DWORD res = WaitForSingleObject(hEv, 0);
    CloseHandle(hEv);
    return (res == WAIT_OBJECT_0);
}

class TaskbarTapSite;
static CRITICAL_SECTION g_sitesLock;
static bool g_sitesLockInit = false;
static TaskbarTapSite* g_sites[32] = { 0 };
static DWORD g_siteThreads[32] = { 0 };
static int g_siteCount = 0;

static void EnsureSitesLock()
{
    if (!g_sitesLockInit)
    {
        InitializeCriticalSection(&g_sitesLock);
        g_sitesLockInit = true;
    }
}

class TaskbarTapSite : public IObjectWithSite, public IVisualTreeServiceCallback2
{
    LONG m_ref;
    IUnknown* m_site;
    IVisualTreeService* m_service;
    InstanceHandle m_targets[64];
    int m_targetCount;
    bool m_timerStarted;
    int m_lastAppliedState; // -1 = unknown, 0 = normal, 1 = clear
    int m_resetRetries;
    int m_tickCounter;

public:
    TaskbarTapSite()
        : m_ref(1), m_site(NULL), m_service(NULL), m_targetCount(0),
          m_timerStarted(false), m_lastAppliedState(-1), m_resetRetries(0), m_tickCounter(0)
    {
    }

    // IUnknown
    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override
    {
        if (!ppv) return E_POINTER;
        if (riid == IID_IUnknown || riid == IID_IObjectWithSite)
            *ppv = static_cast<IObjectWithSite*>(this);
        else if (riid == __uuidof(IVisualTreeServiceCallback) || riid == __uuidof(IVisualTreeServiceCallback2))
            *ppv = static_cast<IVisualTreeServiceCallback2*>(this);
        else
        {
            *ppv = NULL;
            return E_NOINTERFACE;
        }
        AddRef();
        return S_OK;
    }

    STDMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&m_ref); }
    STDMETHODIMP_(ULONG) Release() override
    {
        ULONG r = InterlockedDecrement(&m_ref);
        if (r == 0) delete this;
        return r;
    }

    // IObjectWithSite
    STDMETHODIMP SetSite(IUnknown* pUnkSite) override
    {
        if (pUnkSite == NULL)
        {
            EnsureSitesLock();
            EnterCriticalSection(&g_sitesLock);
            for (int i = 0; i < g_siteCount; i++)
            {
                if (g_sites[i] == this)
                {
                    g_sites[i] = g_sites[g_siteCount - 1];
                    g_siteThreads[i] = g_siteThreads[g_siteCount - 1];
                    g_sites[g_siteCount - 1] = NULL;
                    g_siteThreads[g_siteCount - 1] = 0;
                    g_siteCount--;
                    break;
                }
            }
            LeaveCriticalSection(&g_sitesLock);
            m_targetCount = 0;
        }
        if (m_service) { m_service->Release(); m_service = NULL; }
        if (m_site) { m_site->Release(); m_site = NULL; }
        m_site = pUnkSite;
        if (m_site)
        {
            m_site->AddRef();
            if (SUCCEEDED(m_site->QueryInterface(__uuidof(IVisualTreeService), (void**)&m_service)) && m_service)
            {
                MarkTapActiveInProcess();
                m_service->AdviseVisualTreeChange(this);
            }
        }
        return S_OK;
    }

    STDMETHODIMP GetSite(REFIID riid, void** ppvSite) override
    {
        if (!ppvSite) return E_POINTER;
        if (!m_site) { *ppvSite = NULL; return E_FAIL; }
        return m_site->QueryInterface(riid, ppvSite);
    }

    void ApplyOpacityToHandle(InstanceHandle h, bool makeClear)
    {
        if (!m_service || h == 0) return;

        unsigned int srcCount = 0, propCount = 0;
        PropertyChainSource* pSources = NULL;
        PropertyChainValue* pProps = NULL;

        HRESULT hr = m_service->GetPropertyValuesChain(h, &srcCount, &pSources, &propCount, &pProps);
        if (FAILED(hr)) return;

        int opacityIndex = -1;
        for (unsigned int i = 0; i < propCount; i++)
        {
            if (pProps[i].PropertyName && wcscmp(pProps[i].PropertyName, L"Opacity") == 0)
            {
                opacityIndex = (int)pProps[i].Index;
                break;
            }
        }

        if (pSources)
        {
            for (unsigned int i = 0; i < srcCount; i++)
            {
                if (pSources[i].TargetType) SysFreeString(pSources[i].TargetType);
                if (pSources[i].Name) SysFreeString(pSources[i].Name);
                if (pSources[i].SrcInfo.FileName) SysFreeString(pSources[i].SrcInfo.FileName);
                if (pSources[i].SrcInfo.Hash) SysFreeString(pSources[i].SrcInfo.Hash);
            }
            CoTaskMemFree(pSources);
        }
        if (pProps)
        {
            for (unsigned int i = 0; i < propCount; i++)
            {
                if (pProps[i].Type) SysFreeString(pProps[i].Type);
                if (pProps[i].DeclaringType) SysFreeString(pProps[i].DeclaringType);
                if (pProps[i].ValueType) SysFreeString(pProps[i].ValueType);
                if (pProps[i].ItemType) SysFreeString(pProps[i].ItemType);
                if (pProps[i].Value) SysFreeString(pProps[i].Value);
                if (pProps[i].PropertyName) SysFreeString(pProps[i].PropertyName);
            }
            CoTaskMemFree(pProps);
        }

        if (opacityIndex >= 0)
        {
            if (makeClear)
            {
                InstanceHandle valHandle = 0;
                BSTR bType = SysAllocString(L"Double");
                BSTR bVal = SysAllocString(L"0");
                if (SUCCEEDED(m_service->CreateInstance(bType, bVal, &valHandle)) && valHandle != 0)
                {
                    m_service->SetProperty(h, valHandle, (unsigned int)opacityIndex);
                }
                SysFreeString(bType);
                SysFreeString(bVal);
            }
            else
            {
                InstanceHandle valHandle = 0;
                BSTR bType = SysAllocString(L"Double");
                BSTR bVal = SysAllocString(L"1");
                if (SUCCEEDED(m_service->CreateInstance(bType, bVal, &valHandle)) && valHandle != 0)
                {
                    m_service->SetProperty(h, valHandle, (unsigned int)opacityIndex);
                }
                m_service->ClearProperty(h, (unsigned int)opacityIndex);
                SysFreeString(bType);
                SysFreeString(bVal);
            }
        }
    }

    void OnUiTimerTick()
    {
        bool wantClear = IsClearWanted();
        int desired = wantClear ? 1 : 0;
        m_tickCounter++;

        if (desired != m_lastAppliedState)
        {
            m_lastAppliedState = desired;
            m_resetRetries = wantClear ? 0 : 6;
            for (int i = 0; i < m_targetCount; i++)
            {
                ApplyOpacityToHandle(m_targets[i], wantClear);
            }
        }
        else if (wantClear && (m_tickCounter % 8 == 0))
        {
            for (int i = 0; i < m_targetCount; i++)
            {
                ApplyOpacityToHandle(m_targets[i], true);
            }
        }
        else if (!wantClear && m_resetRetries > 0)
        {
            m_resetRetries--;
            for (int i = 0; i < m_targetCount; i++)
            {
                ApplyOpacityToHandle(m_targets[i], false);
            }
        }
    }

    static VOID CALLBACK StaticTimerProc(HWND, UINT, UINT_PTR, DWORD)
    {
        DWORD tid = GetCurrentThreadId();
        TaskbarTapSite* todo[32];
        int todoCount = 0;

        EnsureSitesLock();
        EnterCriticalSection(&g_sitesLock);
        for (int i = 0; i < g_siteCount && todoCount < 32; i++)
        {
            if (g_siteThreads[i] == tid && g_sites[i] != NULL)
            {
                g_sites[i]->AddRef();
                todo[todoCount++] = g_sites[i];
            }
        }
        LeaveCriticalSection(&g_sitesLock);

        for (int i = 0; i < todoCount; i++)
        {
            todo[i]->OnUiTimerTick();
            todo[i]->Release();
        }
    }

    // IVisualTreeServiceCallback
    STDMETHODIMP OnVisualTreeChange(ParentChildRelation, VisualElement element, VisualMutationType mutationType) override
    {
        if (!m_timerStarted)
        {
            m_timerStarted = true;
            EnsureSitesLock();
            EnterCriticalSection(&g_sitesLock);
            if (g_siteCount < 32)
            {
                g_sites[g_siteCount] = this;
                g_siteThreads[g_siteCount] = GetCurrentThreadId();
                g_siteCount++;
            }
            LeaveCriticalSection(&g_sitesLock);
            SetTimer(NULL, 0, 120, StaticTimerProc);
        }

        if (mutationType == Remove)
        {
            for (int i = 0; i < m_targetCount; i++)
            {
                if (m_targets[i] == element.Handle)
                {
                    m_targets[i] = m_targets[m_targetCount - 1];
                    m_targetCount--;
                    break;
                }
            }
            return S_OK;
        }

        if (mutationType == Add && element.Name != NULL)
        {
            if (wcscmp(element.Name, L"BackgroundFill") == 0 ||
                wcscmp(element.Name, L"BackgroundStroke") == 0)
            {
                bool exists = false;
                for (int i = 0; i < m_targetCount; i++)
                {
                    if (m_targets[i] == element.Handle) { exists = true; break; }
                }
                if (!exists && m_targetCount < 64)
                {
                    m_targets[m_targetCount++] = element.Handle;
                }
                ApplyOpacityToHandle(element.Handle, IsClearWanted());
            }
        }
        return S_OK;
    }

    // IVisualTreeServiceCallback2
    STDMETHODIMP OnElementStateChanged(InstanceHandle, VisualElementState, LPCWSTR) override
    {
        return S_OK;
    }
};

class TapClassFactory : public IClassFactory
{
public:
    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override
    {
        if (!ppv) return E_POINTER;
        if (riid == IID_IUnknown || riid == IID_IClassFactory)
        {
            *ppv = static_cast<IClassFactory*>(this);
            return S_OK;
        }
        *ppv = NULL;
        return E_NOINTERFACE;
    }
    STDMETHODIMP_(ULONG) AddRef() override { return 2; }
    STDMETHODIMP_(ULONG) Release() override { return 1; }

    STDMETHODIMP CreateInstance(IUnknown* pUnkOuter, REFIID riid, void** ppvObject) override
    {
        if (pUnkOuter) return CLASS_E_NOAGGREGATION;
        TaskbarTapSite* site = new TaskbarTapSite();
        HRESULT hr = site->QueryInterface(riid, ppvObject);
        site->Release();
        return hr;
    }
    STDMETHODIMP LockServer(BOOL) override { return S_OK; }
};

static TapClassFactory g_factory;

extern "C" HRESULT __stdcall DllGetClassObject(REFCLSID rclsid, REFIID riid, LPVOID* ppv)
{
    if (rclsid == CLSID_TaskbarTap)
        return g_factory.QueryInterface(riid, ppv);
    return CLASS_E_CLASSNOTAVAILABLE;
}

extern "C" HRESULT __stdcall DllCanUnloadNow()
{
    return S_FALSE;
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID)
{
    if (ul_reason_for_call == DLL_PROCESS_ATTACH)
        DisableThreadLibraryCalls(hModule);
    return TRUE;
}
