// Guids.cs
// MUST match guids.h

using System;

namespace Resurrect
{
    static class GuidList
    {
        public const string guidResurrectPkgString = "ae98c9e5-8e14-4c92-b45a-c4fd24a498ef";
        public const string guidResurrectCmdSetString = "b4c5fb60-9e6d-438e-a36f-6edb60e0260f";

        public static readonly Guid guidResurrectCmdSet = new Guid(guidResurrectCmdSetString);
    };
}