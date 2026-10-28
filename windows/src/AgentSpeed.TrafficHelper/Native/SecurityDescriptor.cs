namespace AgentSpeed.TrafficHelper.Native;

/// <summary>A self-relative security descriptor made from SDDL (LocalFree on dispose).</summary>
internal sealed unsafe class SecurityDescriptor : IDisposable
{
    private void* _descriptor;

    private SecurityDescriptor(void* descriptor)
    {
        _descriptor = descriptor;
    }

    public void* Pointer => _descriptor;

    public static SecurityDescriptor? FromSddl(string sddl, out uint error)
    {
        void* descriptor = null;
        if (Advapi32.ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl, Advapi32.SddlRevision1, &descriptor, null) == 0)
        {
            error = Kernel32.LastError;
            return null;
        }

        error = 0;
        return new SecurityDescriptor(descriptor);
    }

    public void Dispose()
    {
        if (_descriptor != null)
        {
            Kernel32.LocalFree((nint)_descriptor);
            _descriptor = null;
        }
    }
}
