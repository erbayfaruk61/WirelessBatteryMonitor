using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using WirelessBatteryMonitor.Constants;

namespace WirelessBatteryMonitor.Readers;

public sealed record HidCollectionInfo(
    string DevicePath,
    ushort VendorId,
    ushort ProductId,
    ushort UsagePage,
    ushort Usage,
    int InputReportLength,
    int OutputReportLength,
    string ProductName);

/// <summary>
/// Harici bağımlılık olmadan Win32 API üzerinden USB HID cihazlarını tarar ve haberleşir.
/// </summary>
public static class HidNative
{
    private const int DigcfPresent = 0x00000002;
    private const int DigcfDeviceInterface = 0x00000010;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileFlagOverlapped = 0x40000000;
    private const int ErrorIoPending = 997;

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public nint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HiddAttributes
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidpCaps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [DllImport("hid.dll")]
    private static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetAttributes(SafeFileHandle hidDeviceObject, ref HiddAttributes attributes);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle hidDeviceObject, out nint preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_FreePreparsedData(nint preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern int HidP_GetCaps(nint preparsedData, out HidpCaps capabilities);

    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool HidD_GetProductString(SafeFileHandle hidDeviceObject, StringBuilder buffer, int bufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_FlushQueue(SafeFileHandle hidDeviceObject);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, nint hwndParent, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(nint deviceInfoSet, nint deviceInfoData, ref Guid interfaceClassGuid, int memberIndex, ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(nint deviceInfoSet, ref SpDeviceInterfaceData deviceInterfaceData, nint deviceInterfaceDetailData, int deviceInterfaceDetailDataSize, out int requiredSize, nint deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(nint deviceInfoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, nint lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, nint hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(SafeFileHandle hFile, byte[] lpBuffer, int nNumberOfBytesToRead, out int lpNumberOfBytesRead, ref NativeOverlapped lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(SafeFileHandle hFile, byte[] lpBuffer, int nNumberOfBytesToWrite, out int lpNumberOfBytesWritten, ref NativeOverlapped lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CancelIoEx(SafeFileHandle hFile, ref NativeOverlapped lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetOverlappedResult(SafeFileHandle hFile, ref NativeOverlapped lpOverlapped, out int lpNumberOfBytesTransferred, bool bWait);

    public static IReadOnlyList<HidCollectionInfo> EnumerateDevices(ushort? filterVid = null)
    {
        var results = new List<HidCollectionInfo>();
        HidD_GetHidGuid(out var hidGuid);

        var infoSet = SetupDiGetClassDevs(ref hidGuid, null, nint.Zero, DigcfPresent | DigcfDeviceInterface);
        if (infoSet == nint.Zero || infoSet == new nint(-1))
            return results;

        try
        {
            var ifaceData = new SpDeviceInterfaceData { cbSize = Marshal.SizeOf<SpDeviceInterfaceData>() };
            for (int i = 0; SetupDiEnumDeviceInterfaces(infoSet, nint.Zero, ref hidGuid, i, ref ifaceData); i++)
            {
                SetupDiGetDeviceInterfaceDetail(infoSet, ref ifaceData, nint.Zero, 0, out int reqSize, nint.Zero);
                if (reqSize <= 0) continue;

                nint detailBuf = Marshal.AllocHGlobal(reqSize);
                try
                {
                    Marshal.WriteInt32(detailBuf, nint.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(infoSet, ref ifaceData, detailBuf, reqSize, out _, nint.Zero))
                        continue;

                    string? path = Marshal.PtrToStringUni(detailBuf + 4);
                    if (string.IsNullOrEmpty(path)) continue;

                    using var handle = CreateFile(path, 0, FileShareRead | FileShareWrite, nint.Zero, OpenExisting, 0, nint.Zero);
                    if (handle.IsInvalid) continue;

                    var attrs = new HiddAttributes { Size = Marshal.SizeOf<HiddAttributes>() };
                    if (!HidD_GetAttributes(handle, ref attrs)) continue;
                    if (filterVid.HasValue && attrs.VendorID != filterVid.Value) continue;

                    if (!HidD_GetPreparsedData(handle, out nint preparsed)) continue;
                    try
                    {
                        if (HidP_GetCaps(preparsed, out var caps) < 0) continue;
                        var sb = new StringBuilder(256);
                        HidD_GetProductString(handle, sb, sb.Capacity);

                        results.Add(new HidCollectionInfo(
                            path,
                            attrs.VendorID,
                            attrs.ProductID,
                            caps.UsagePage,
                            caps.Usage,
                            caps.InputReportByteLength,
                            caps.OutputReportByteLength,
                            sb.ToString().Trim()));
                    }
                    finally
                    {
                        HidD_FreePreparsedData(preparsed);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detailBuf);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(infoSet);
        }

        return results;
    }

    public static SafeFileHandle? OpenReadWrite(string devicePath)
    {
        var handle = CreateFile(
            devicePath,
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite,
            nint.Zero,
            OpenExisting,
            FileFlagOverlapped,
            nint.Zero);

        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }
        return handle;
    }

    public static bool Flush(SafeFileHandle handle) => HidD_FlushQueue(handle);

    public static bool WriteReport(SafeFileHandle handle, byte[] data, int outputReportLength, int timeoutMs = AppConstants.HidTimeoutMs)
    {
        if (outputReportLength <= 0) return false;
        var buffer = new byte[outputReportLength];
        Array.Copy(data, buffer, Math.Min(data.Length, buffer.Length));

        using var evt = new ManualResetEvent(false);
        var overlapped = new NativeOverlapped { EventHandle = evt.SafeWaitHandle.DangerousGetHandle() };

        if (!WriteFile(handle, buffer, buffer.Length, out _, ref overlapped))
        {
            if (Marshal.GetLastWin32Error() != ErrorIoPending) return false;
            if (!evt.WaitOne(timeoutMs))
            {
                CancelIoEx(handle, ref overlapped);
                return false;
            }
            return GetOverlappedResult(handle, ref overlapped, out _, false);
        }
        return true;
    }

    public static byte[]? ReadReport(SafeFileHandle handle, int inputReportLength, int timeoutMs = AppConstants.HidTimeoutMs)
    {
        if (inputReportLength <= 0) return null;
        var buffer = new byte[inputReportLength];

        using var evt = new ManualResetEvent(false);
        var overlapped = new NativeOverlapped { EventHandle = evt.SafeWaitHandle.DangerousGetHandle() };

        if (!ReadFile(handle, buffer, buffer.Length, out int readBytes, ref overlapped))
        {
            if (Marshal.GetLastWin32Error() != ErrorIoPending) return null;
            if (!evt.WaitOne(timeoutMs))
            {
                CancelIoEx(handle, ref overlapped);
                return null;
            }
            if (!GetOverlappedResult(handle, ref overlapped, out readBytes, false) || readBytes <= 0)
                return null;
        }

        return buffer[..readBytes];
    }
}
