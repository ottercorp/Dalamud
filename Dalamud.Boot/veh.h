#pragma once

namespace veh
{
    bool add_handler(bool doFullDump, const std::string& workingDirectory);
    void set_managed_stacktrace_fun(LPVOID fun);
    bool remove_handler();
    void raise_external_event(const std::wstring& info);
}
