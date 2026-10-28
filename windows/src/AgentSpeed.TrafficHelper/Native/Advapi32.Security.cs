using System.Runtime.InteropServices;

namespace AgentSpeed.TrafficHelper.Native;

/// <summary>Security descriptor, SID and token entry points.</summary>
internal static unsafe partial class Advapi32
{
    public const uint SddlRevision1 = 1;
    public const uint OwnerSecurityInformation = 0x1;
    public const uint DaclSecurityInformation = 0x4;
    public const uint SeFileObject = 1;

    public const uint TokenQuery = 0x0008;
    public const int TokenUserClass = 1;
    public const int TokenElevationTypeClass = 18;
    public const int TokenLinkedTokenClass = 19;

    public const int TokenElevationTypeDefault = 1;

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int ConvertStringSecurityDescriptorToSecurityDescriptorW(
        string sddl, uint revision, void** descriptor, uint* size);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int ConvertStringSidToSidW(string sid, void** binarySid);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint GetNamedSecurityInfoW(
        string objectName, uint objectType, uint securityInfo, void** owner, void** group, void** dacl, void** sacl,
        void** descriptor);

    [LibraryImport("advapi32.dll")]
    public static partial uint GetSecurityInfo(
        nint handle, uint objectType, uint securityInfo, void** owner, void** group, void** dacl, void** sacl, void** descriptor);

    [LibraryImport("advapi32.dll")]
    public static partial uint SetSecurityInfo(nint handle, uint objectType, uint securityInfo, void* owner, void* group, void* dacl, void* sacl);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    public static partial int ConvertSecurityDescriptorToStringSecurityDescriptorW(
        void* descriptor, uint revision, uint securityInfo, char** text, uint* length);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    public static partial int GetSecurityDescriptorDacl(void* descriptor, int* present, void** dacl, int* defaulted);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int LookupAccountSidW(
        string? systemName, void* sid, char* name, uint* nameLength, char* domain, uint* domainLength, int* use);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    public static partial int GetLengthSid(void* sid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    public static partial int IsValidSid(void* sid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    public static partial int CheckTokenMembership(nint token, void* sid, int* isMember);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    public static partial int OpenProcessToken(nint process, uint access, nint* token);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    public static partial int GetTokenInformation(nint token, int informationClass, void* buffer, uint length, uint* returned);

    /// <summary>The SDDL of the requested parts of a descriptor, or null.</summary>
    public static string? DescriptorToSddl(void* descriptor, uint securityInfo)
    {
        char* text;
        uint length;
        if (ConvertSecurityDescriptorToStringSecurityDescriptorW(descriptor, SddlRevision1, securityInfo, &text, &length) == 0)
        {
            return null;
        }

        try
        {
            return new string(text);
        }
        finally
        {
            Kernel32.LocalFree((nint)text);
        }
    }
}
