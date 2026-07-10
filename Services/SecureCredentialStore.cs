using System.Runtime.InteropServices;

namespace Clicky.Windows.Services;

/// <summary>
/// Stores provider secrets in Windows Credential Manager. Settings JSON only
/// contains non-secret endpoint and model metadata.
/// </summary>
public sealed class SecureCredentialStore
{
    private const string PrimaryTargetName = "Clicky.Windows/primary-provider";
    private const string AudioTargetName = "Clicky.Windows/audio-provider";
    private const string EmailTargetName = "Clicky.Windows/email-smtp";
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;

    public string? ReadApiKey() => Read(PrimaryTargetName);

    public string? ReadAudioApiKey() => Read(AudioTargetName);

    public string? ReadEmailPassword() => Read(EmailTargetName);

    public void WriteApiKey(string apiKey) => Write(PrimaryTargetName, apiKey);

    public void WriteAudioApiKey(string apiKey) => Write(AudioTargetName, apiKey);

    public void WriteEmailPassword(string password) => Write(EmailTargetName, password);

    public void DeleteApiKey() => Delete(PrimaryTargetName);

    public void DeleteAudioApiKey() => Delete(AudioTargetName);

    public void DeleteEmailPassword() => Delete(EmailTargetName);

    private static string? Read(string targetName)
    {
        if (!CredRead(targetName, CredTypeGeneric, 0, out var credentialPointer))
        {
            return null;
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return null;
            }

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    private static void Write(string targetName, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Delete(targetName);
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(apiKey.Trim());
        var blobPointer = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blobPointer, bytes.Length);
            var credential = new NativeCredential
            {
                Type = CredTypeGeneric,
                TargetName = targetName,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blobPointer,
                Persist = CredPersistLocalMachine,
                UserName = Environment.UserName
            };

            if (!CredWrite(ref credential, 0))
            {
                throw new InvalidOperationException($"Windows Credential Manager rejected the API key (error {Marshal.GetLastWin32Error()}).");
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(blobPointer);
            Array.Clear(bytes, 0, bytes.Length);
        }
    }

    private static void Delete(string targetName)
    {
        _ = CredDelete(targetName, CredTypeGeneric, 0);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, int reservedFlag, out IntPtr credentialPointer);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr credentialPointer);
}
