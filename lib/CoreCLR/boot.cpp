#define WIN32_LEAN_AND_MEAN

#include <cstdio>
#include <filesystem>
#include <Windows.h>
#include <Lmcons.h>
#include <Shlobj.h>
#include "CoreCLR.h"
#include "..\..\Dalamud.Boot\logging.h"

FILE* g_CmdStream;
void ConsoleSetup(const std::wstring console_name)
{
    if (!AllocConsole())
        return;

    SetConsoleTitleW(console_name.c_str());
    freopen_s(&g_CmdStream, "CONOUT$", "w", stdout);
    freopen_s(&g_CmdStream, "CONOUT$", "w", stderr);
    freopen_s(&g_CmdStream, "CONIN$", "r", stdin);
    SetConsoleOutputCP(CP_UTF8);
}

void ConsoleTeardown()
{
    FreeConsole();
}

std::optional<CoreCLR> g_clr;

static wchar_t* GetRuntimePath()
{
    int result;
    std::wstring buffer;
    wchar_t* runtime_path;
    wchar_t* _appdata;
    DWORD username_len = UNLEN + 1;
    wchar_t username[UNLEN + 1];

    buffer.resize(0);
    result = GetEnvironmentVariableW(L"DALAMUD_RUNTIME", &buffer[0], 0);

    if (result)
    {
        buffer.resize(result); // The first pass returns the required length
        result = GetEnvironmentVariableW(L"DALAMUD_RUNTIME", &buffer[0], result);
        return _wcsdup(buffer.c_str());
    }

    // Detect Windows first
    result = SHGetKnownFolderPath(FOLDERID_RoamingAppData, KF_FLAG_DEFAULT, nullptr, &_appdata);

    if (result != 0)
    {
        logging::E("Unable to get RoamingAppData path (err={})", result);
        return NULL;
    }

    std::filesystem::path fs_app_data(_appdata);
    runtime_path = _wcsdup(fs_app_data.append("XIVLauncherCN").append("runtime").c_str());
    if (std::filesystem::exists(runtime_path))
        return runtime_path;
    free(runtime_path);

    // Next XLCore on Linux
    result = GetUserNameW(username, &username_len);
    if (result != 0)
    {
        logging::E("Unable to get user name (err={})", result);
        return NULL;
    }

    std::filesystem::path homeDir = L"Z:\\home\\" + std::wstring(username);
    runtime_path = _wcsdup(homeDir.append(".xlcore").append("runtime").c_str());
    if (std::filesystem::exists(runtime_path))
        return runtime_path;
    free(runtime_path);

    // Finally XOM
    homeDir = L"Z:\\Users\\" + std::wstring(username);
    runtime_path = _wcsdup(homeDir.append("Library").append("Application Suppor").append("XIV on Mac").append("runtime").c_str());
    if (std::filesystem::exists(runtime_path))
        return runtime_path;
    free(runtime_path);

    return NULL;
}

HRESULT InitializeClrAndGetEntryPoint(
    void* calling_module,
    bool enable_etw,
    std::wstring runtimeconfig_path,
    std::wstring module_path,
    std::wstring entrypoint_assembly_name,
    std::wstring entrypoint_method_name,
    std::wstring entrypoint_delegate_type_name,
    void** entrypoint_fn)
{
    g_clr.emplace(calling_module);

    int result;
    SetEnvironmentVariable(L"DOTNET_MULTILEVEL_LOOKUP", L"0");

    SetEnvironmentVariable(L"COMPLUS_ForceENC", L"1");
    SetEnvironmentVariable(L"DOTNET_ForceENC", L"1");

    // Enable Dynamic PGO
    SetEnvironmentVariable(L"DOTNET_TieredPGO", L"1");
    SetEnvironmentVariable(L"DOTNET_TC_QuickJitForLoops", L"1");
    SetEnvironmentVariable(L"DOTNET_ReadyToRun", L"1");

    // WINE does not support QUIC and we don't need it
    SetEnvironmentVariable(L"DOTNET_SYSTEM_NET_HTTP_SOCKETSHTTPHANDLER_HTTP3SUPPORT", L"0");

    SetEnvironmentVariable(L"COMPlus_ETWEnabled", enable_etw ? L"1" : L"0");

    wchar_t* dotnet_path = GetRuntimePath();

    if (!dotnet_path || !std::filesystem::exists(dotnet_path))
    {
        logging::E("Error: Unable to find .NET runtime path");
        return HRESULT_FROM_WIN32(ERROR_PATH_NOT_FOUND);
    }

    // =========================================================================== //

    logging::I("with dotnet_path: {}", dotnet_path);
    logging::I("with config_path: {}", runtimeconfig_path);
    logging::I("with module_path: {}", module_path);

    get_hostfxr_parameters init_parameters
    {
        sizeof(get_hostfxr_parameters),
        nullptr,
        dotnet_path,
    };

    logging::I("Loading hostfxr...");
    if ((result = g_clr->load_hostfxr(&init_parameters)) != 0)
    {
        logging::E("Failed to load the `hostfxr` library (err=0x{:08x})", result);
        return result;
    }
    logging::I("Done!");

    // =========================================================================== //

    hostfxr_initialize_parameters runtime_parameters
    {
        sizeof(hostfxr_initialize_parameters),
        module_path.c_str(),
        dotnet_path,
    };

    logging::I("Loading coreclr... ");
    if ((result = g_clr->load_runtime(runtimeconfig_path, &runtime_parameters)) != 0)
    {
        logging::E("Failed to load coreclr (err=0x{:08X})", static_cast<uint32_t>(result));
        return result;
    }
    logging::I("Done!");

    // =========================================================================== //

    logging::I("Loading module from {}...", module_path.c_str());
    if ((result = g_clr->load_assembly_and_get_function_pointer(
        module_path.c_str(),
        entrypoint_assembly_name.c_str(),
        entrypoint_method_name.c_str(),
        entrypoint_delegate_type_name.c_str(),
        nullptr, entrypoint_fn)) != 0)
    {
        logging::E("Failed to load module (err=0x{:X})", static_cast<uint32_t>(result));
        return result;
    }
    logging::I("Done!");

    // =========================================================================== //

    return S_OK;
}
