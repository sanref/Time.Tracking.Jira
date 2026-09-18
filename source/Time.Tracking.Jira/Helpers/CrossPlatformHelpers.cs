/**************************************************************************
Copyright 2016 Carsten Gehling

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
**************************************************************************/
using System;

namespace Time.Tracking.Jira
{
    internal static class CrossPlatformHelpers
    {
        public static bool IsWindowsEnvironment()
        {
            // Antes se miraba Environment.OSVersion.Platform contra los distintos PlatformID de
            // Windows, que en .NET moderno ya no distinguen nada (Win32S, WinCE y Win32Windows
            // no son valores alcanzables).
            return OperatingSystem.IsWindows();
        }
    }
}
