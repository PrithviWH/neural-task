using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NeuralTask.Plugins.Gpu;

public record GpuAdapterIdentity(
    string Name,
    string Luid,
    long DedicatedBytes,
    uint VendorId,
    uint DeviceId,
    bool IsSoftware);

public interface IGpuAdapterResolver
{
    IReadOnlyList<GpuAdapterIdentity> DiscoverAdapters();
}

public class DxgiAdapterResolver : IGpuAdapterResolver
{
    public static readonly DxgiAdapterResolver Instance = new();

    [ComImport]
    [Guid("7b7166ec-21c7-44ae-b21a-c9ae321ae369")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIFactory
    {
        // IDXGIObject
        [PreserveSig] int SetPrivateData(ref Guid Name, uint DataSize, IntPtr pData);
        [PreserveSig] int SetPrivateDataInterface(ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        [PreserveSig] int GetPrivateData(ref Guid Name, ref uint pDataSize, IntPtr pData);
        [PreserveSig] int GetParent(ref Guid riid, out IntPtr ppParent);

        // IDXGIFactory
        [PreserveSig] int EnumAdapters(uint Adapter, out IntPtr ppAdapter);
        [PreserveSig] int MakeWindowAssociation(IntPtr WindowHandle, uint Flags);
        [PreserveSig] int GetWindowAssociation(out IntPtr pWindowHandle);
        [PreserveSig] int CreateSwapChain(IntPtr pDevice, IntPtr pDesc, out IntPtr ppSwapChain);
        [PreserveSig] int CreateSoftwareAdapter(IntPtr Module, out IntPtr ppAdapter);
    }

    [ComImport]
    [Guid("2411e7e1-12ac-4ccf-bd14-9798e8534dc0")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIAdapter
    {
        // IDXGIObject
        [PreserveSig] int SetPrivateData(ref Guid Name, uint DataSize, IntPtr pData);
        [PreserveSig] int SetPrivateDataInterface(ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        [PreserveSig] int GetPrivateData(ref Guid Name, ref uint pDataSize, IntPtr pData);
        [PreserveSig] int GetParent(ref Guid riid, out IntPtr ppParent);

        // IDXGIAdapter
        [PreserveSig] int EnumOutputs(uint Output, out IntPtr ppOutput);
        [PreserveSig] int GetDesc(out DXGI_ADAPTER_DESC pDesc);
        [PreserveSig] int CheckInterfaceSupport(ref Guid InterfaceName, out long pUMDVersion);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public UIntPtr DedicatedVideoMemory;
        public UIntPtr DedicatedSystemMemory;
        public UIntPtr SharedSystemMemory;
        public uint LuidLow;
        public int LuidHigh;
    }

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory(ref Guid riid, out IntPtr ppFactory);

    private const int S_OK = 0;

    public IReadOnlyList<GpuAdapterIdentity> DiscoverAdapters()
    {
        var list = new List<GpuAdapterIdentity>();

        if (!OperatingSystem.IsWindows())
        {
            return list;
        }

        try
        {
            var factoryGuid = new Guid("7b7166ec-21c7-44ae-b21a-c9ae321ae369");
            int hr = CreateDXGIFactory(ref factoryGuid, out var pFactory);
            if (hr != S_OK || pFactory == IntPtr.Zero)
            {
                return list;
            }

            IDXGIFactory factory;
            try
            {
                factory = (IDXGIFactory)Marshal.GetObjectForIUnknown(pFactory);
            }
            finally
            {
                Marshal.Release(pFactory);
            }

            try
            {
                uint index = 0;
                while (factory.EnumAdapters(index++, out var pAdapter) == S_OK && pAdapter != IntPtr.Zero)
                {
                    IDXGIAdapter adapter;
                    try
                    {
                        adapter = (IDXGIAdapter)Marshal.GetObjectForIUnknown(pAdapter);
                    }
                    finally
                    {
                        Marshal.Release(pAdapter);
                    }

                    try
                    {
                        if (adapter.GetDesc(out var desc) == S_OK)
                        {
                            string luid = $"0x{desc.LuidHigh:x8}_0x{desc.LuidLow:x8}";
                            long dedicatedBytes = (long)desc.DedicatedVideoMemory.ToUInt64();
                            // Basic render driver / WARP has VendorId 0x1414 (Microsoft) and DeviceId 0x8c
                            bool isSoftware = desc.VendorId == 0x1414 && desc.DeviceId == 0x8c;

                            list.Add(new GpuAdapterIdentity(
                                Name: desc.Description ?? string.Empty,
                                Luid: luid,
                                DedicatedBytes: dedicatedBytes,
                                VendorId: desc.VendorId,
                                DeviceId: desc.DeviceId,
                                IsSoftware: isSoftware
                            ));
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(adapter);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"DXGI query failed: {ex.Message}");
        }

        return list;
    }
}
