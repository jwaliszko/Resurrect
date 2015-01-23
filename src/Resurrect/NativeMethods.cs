using System;
using System.Runtime.InteropServices;

namespace Resurrect
{
    internal static class NativeMethods
    {
        /// <summary>
        ///  Retrieves a specified type of information about an access token. The calling process must have appropriate access rights to obtain the information.
        /// </summary>  
        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool GetTokenInformation(IntPtr tokenHandle, TokenInformationClass tokenInformationClass, IntPtr tokenInformation, int tokenInformationLength, out int returnLength);
    }

    /// <summary>
    /// Passed to <see cref="NativeMethods.GetTokenInformation"/> to specify what
    /// information about the token to return.
    /// </summary>
    enum TokenInformationClass
    {
        TokenUser = 1,
        TokenGroups,
        TokenPrivileges,
        TokenOwner,
        TokenPrimaryGroup,
        TokenDefaultDacl,
        TokenSource,
        TokenType,
        TokenImpersonationLevel,
        TokenStatistics,
        TokenRestrictedSids,
        TokenSessionId,
        TokenGroupsAndPrivileges,
        TokenSessionReference,
        TokenSandBoxInert,
        TokenAuditPolicy,
        TokenOrigin,
        TokenElevationType,
        TokenLinkedToken,
        TokenElevation,
        TokenHasRestrictions,
        TokenAccessInformation,
        TokenVirtualizationAllowed,
        TokenVirtualizationEnabled,
        TokenIntegrityLevel,
        TokenUiAccess,
        TokenMandatoryPolicy,
        TokenLogonSid,
        MaxTokenInfoClass
    }

    /// <summary>
    /// The elevation type for a user token.
    /// </summary>
    enum TokenElevationType
    {
        TokenElevationTypeDefault = 1,  // The token does not have a linked token.
        TokenElevationTypeFull,         // The token is an elevated token.
        TokenElevationTypeLimited       // The token is a limited token.
    }
}
