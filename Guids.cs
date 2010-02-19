// Guids.cs
// MUST match guids.h
using System;

namespace NoahRichards.AlignAssignments
{
    static class GuidList
    {
        public const string guidAlignAssignmentsPkgString = "1f50f37e-21c8-41c1-8f19-02e7973e764b";
        public const string guidAlignAssignmentsCmdSetString = "ba0f8e5d-6d3a-46d9-b8cf-cd585018a493";

        public static readonly Guid guidAlignAssignmentsCmdSet = new Guid(guidAlignAssignmentsCmdSetString);
    };
}