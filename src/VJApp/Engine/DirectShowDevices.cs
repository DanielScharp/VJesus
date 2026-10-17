using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace VJApp.Engine;

/// <summary>
/// Lista as entradas de vídeo do Windows pelo próprio DirectShow (câmeras USB, placas de captura,
/// Câmera Virtual do OBS…). É a mesma lista que o FFmpeg abre com "video=Nome".
/// </summary>
static class DirectShowDevices
{
    static readonly Guid SystemDeviceEnum = new("62BE5D10-60EB-11d0-BD3B-00A0C911CE86");
    static readonly Guid VideoInputDeviceCategory = new("860BB310-5D01-11d0-BD3B-00A0C911CE86");

    [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICreateDevEnum
    {
        [PreserveSig]
        int CreateClassEnumerator([In] ref Guid category, out IEnumMoniker? enumerator, int flags);
    }

    [ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyBag
    {
        [PreserveSig]
        int Read([MarshalAs(UnmanagedType.LPWStr)] string name, out object? value, IntPtr errorLog);
        [PreserveSig]
        int Write([MarshalAs(UnmanagedType.LPWStr)] string name, ref object value);
    }

    public static List<string> VideoInputs()
    {
        var names = new List<string>();
        try
        {
            var type = Type.GetTypeFromCLSID(SystemDeviceEnum);
            if (type == null || Activator.CreateInstance(type) is not ICreateDevEnum devEnum) return names;
            var category = VideoInputDeviceCategory;
            // S_FALSE (1) = categoria vazia: nenhuma câmera
            if (devEnum.CreateClassEnumerator(ref category, out var monikers, 0) != 0 || monikers == null) return names;
            var one = new IMoniker[1];
            while (monikers.Next(1, one, IntPtr.Zero) == 0)
            {
                var m = one[0];
                try
                {
                    var bagId = typeof(IPropertyBag).GUID;
                    m.BindToStorage(null!, null, ref bagId, out var obj);
                    if (obj is IPropertyBag bag && bag.Read("FriendlyName", out var value, IntPtr.Zero) == 0 && value is string name && name.Length > 0)
                        names.Add(name);
                }
                catch { /* aparelho sem nome legível: pula */ }
                finally { Marshal.ReleaseComObject(m); }
            }
            Marshal.ReleaseComObject(monikers);
        }
        catch { /* DirectShow indisponível */ }
        return names;
    }
}
