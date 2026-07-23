using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Kuroko;

/// <summary>
/// DirectShow の映像入力デバイスを列挙する。列挙順は OpenCV の CAP_DSHOW index と一致する前提で、
/// リストの位置＝エンジンへ渡すカメラindex とする。仮想カメラ(Unity Video Capture 等)も含まれる。
/// </summary>
public static class CameraEnumerator
{
    public record Device(int Index, string Name);

    public static List<Device> List()
    {
        var devices = new List<Device>();
        try
        {
            var devEnumType = Type.GetTypeFromCLSID(CLSID_SystemDeviceEnum)!;
            var devEnum = (ICreateDevEnum)Activator.CreateInstance(devEnumType)!;
            var category = CLSID_VideoInputDeviceCategory;
            devEnum.CreateClassEnumerator(ref category, out IEnumMoniker? enumMoniker, 0);
            if (enumMoniker is null)
            {
                return devices; // デバイスなし
            }

            var monikers = new IMoniker[1];
            int index = 0;
            while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
            {
                string name = ReadFriendlyName(monikers[0]) ?? $"Camera {index}";
                devices.Add(new Device(index, name));
                Marshal.ReleaseComObject(monikers[0]);
                index++;
            }
            Marshal.ReleaseComObject(enumMoniker);
            Marshal.ReleaseComObject(devEnum);
        }
        catch (Exception ex)
        {
            Logger.Error("Camera enumeration failed", ex);
        }
        return devices;
    }

    /// <summary>名前に部分一致するデバイスの index を返す（見つからなければ -1）。</summary>
    public static int FindIndexByName(string substring)
    {
        foreach (var d in List())
        {
            if (d.Name.Contains(substring, StringComparison.OrdinalIgnoreCase))
            {
                return d.Index;
            }
        }
        return -1;
    }

    private static string? ReadFriendlyName(IMoniker moniker)
    {
        try
        {
            var bagId = typeof(IPropertyBag).GUID;
            moniker.BindToStorage(null!, null!, ref bagId, out object bagObj);
            var bag = (IPropertyBag)bagObj;
            object value = "";
            int hr = bag.Read("FriendlyName", ref value, IntPtr.Zero);
            Marshal.ReleaseComObject(bag);
            return hr == 0 ? value as string : null;
        }
        catch
        {
            return null;
        }
    }

    private static readonly Guid CLSID_SystemDeviceEnum = new("62BE5D10-60EB-11d0-BD3B-00A0C911CE86");
    private static readonly Guid CLSID_VideoInputDeviceCategory = new("860BB310-5D01-11d0-BD3B-00A0C911CE86");

    [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICreateDevEnum
    {
        [PreserveSig]
        int CreateClassEnumerator([In] ref Guid pType, [Out] out IEnumMoniker? ppEnumMoniker, [In] int dwFlags);
    }

    [ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyBag
    {
        [PreserveSig]
        int Read([MarshalAs(UnmanagedType.LPWStr)] string pszPropName, ref object pVar, IntPtr pErrorLog);

        [PreserveSig]
        int Write([MarshalAs(UnmanagedType.LPWStr)] string pszPropName, ref object pVar);
    }
}
