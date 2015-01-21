// Guids.cs
// MUST match guids.h

using System;

namespace Resurrect
{
    internal static class Constants
    {
        // guids
        public const string GuidResurrectPkgString = "ae98c9e5-8e14-4c92-b45a-c4fd24a498ef";
        public const string GuidResurrectCmdSetString = "b4c5fb60-9e6d-438e-a36f-6edb60e0260f";
        public const string GuidOutputPaneString = "9fcf11a3-d076-45f0-9db8-b81225eab8ef";

        public static readonly Guid GuidResurrectCmdSet = new Guid(GuidResurrectCmdSetString);
        public static readonly Guid GuidOutputPane = new Guid(GuidOutputPaneString);  

        // commands
        public const int CmdidResurrect = 0x0100;
        public const int CmdidAutoAttach = 0x0200;
    };
}