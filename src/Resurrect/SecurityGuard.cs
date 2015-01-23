using System;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Resurrect
{
    internal static class SecurityGuard
    {
        public static bool HasAdminRights
        {
            get
            {
                var identity = WindowsIdentity.GetCurrent();
                if (identity == null) 
                    throw new InvalidOperationException("Couldn't get current user identity.");
                var principal = new WindowsPrincipal(identity);

                // Check if this user has the Administrator role. If they do, return immediately.
                // If UAC is on, and the process is not elevated, then this will actually return false.
                if (principal.IsInRole(WindowsBuiltInRole.Administrator)) 
                    return true;

                // If we're not running in Vista onwards, we don't have to worry about checking for UAC.
                if (Environment.OSVersion.Platform != PlatformID.Win32NT || Environment.OSVersion.Version.Major < 6)
                    return false;   // Operating system does not support UAC; skipping elevation check.

                var tokenInfLength = Marshal.SizeOf(typeof(int));
                var tokenInformation = Marshal.AllocHGlobal(tokenInfLength);

                try
                {
                    var token = identity.Token;
                    var result = NativeMethods.GetTokenInformation(token, TokenInformationClass.TokenElevationType, tokenInformation, tokenInfLength, out tokenInfLength);

                    if (!result)
                    {
                        var exception = Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error());
                        throw new InvalidOperationException("Couldn't get token information.", exception);
                    }

                    var elevationType = (TokenElevationType)Marshal.ReadInt32(tokenInformation);
                    return elevationType == TokenElevationType.TokenElevationTypeFull;
                }
                finally
                {
                    if (tokenInformation != IntPtr.Zero) 
                        Marshal.FreeHGlobal(tokenInformation);
                }
            }
        }
    }
}
