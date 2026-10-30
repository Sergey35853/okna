using System;
using System.Runtime.InteropServices;

namespace Okna
{
    /// <summary>
    /// Live window capture through Windows Graphics Capture and a D3D11
    /// readback. PrintWindow cannot render Explorer's client area (it comes
    /// back black), so Explorer is captured through the compositor instead.
    /// One instance lives on the live-capture thread for a single drag.
    /// </summary>
    internal sealed class WgcCapture : IDisposable
    {
        private const int RO_INIT_MULTITHREADED = 1;
        private const int D3D_DRIVER_TYPE_HARDWARE = 1;
        private const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
        private const uint D3D11_SDK_VERSION = 7;
        private const int DXGI_FORMAT_B8G8R8A8_UNORM = 87;
        private const int D3D11_USAGE_STAGING = 3;
        private const uint D3D11_CPU_ACCESS_READ = 0x20000;
        private const int D3D11_MAP_READ = 1;

        private static readonly Guid IID_IGraphicsCaptureItemInterop = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
        private static readonly Guid IID_IGraphicsCaptureItem = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
        private static readonly Guid IID_IFramePoolStatics2 = new Guid("589b103f-6bbc-5df5-a991-02e28b3b66d5");
        private static readonly Guid IID_IDirect3DDxgiInterfaceAccess = new Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
        private static readonly Guid IID_IDXGIDevice = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
        private static readonly Guid IID_ID3D11Texture2D = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
        private static readonly Guid IID_IClosable = new Guid("30d5a829-7fa4-4026-83bb-d75bae4ea99e");

        private IntPtr _d3dDevice;
        private IntPtr _d3dContext;
        private IntPtr _winrtDevice;
        private IGraphicsCaptureItem _item;
        private IDirect3D11CaptureFramePool _pool;
        private IGraphicsCaptureSession _session;
        private IntPtr _staging;
        private int _texW;
        private int _texH;
        private int _texFormat;
        private bool _roInitialized;

        private CreateTexture2DDelegate _createTexture;
        private CopyResourceDelegate _copyResource;
        private MapDelegate _map;
        private UnmapDelegate _unmap;

        private static int _supported = -1;

        /// <summary>
        /// True when Windows Graphics Capture exists on this system. The class
        /// arrived in Windows 10 1803 (build 17134); on Windows 8/8.1 and older
        /// Windows 10 builds the activation factory is missing, and starting a
        /// capture would only create a D3D device and fail. The answer is
        /// probed once and cached.
        /// </summary>
        public static bool IsSupported
        {
            get
            {
                int value = _supported;
                if (value >= 0) return value == 1;
                bool ok = ProbeSupport();
                _supported = ok ? 1 : 0;
                return ok;
            }
        }

        private static bool ProbeSupport()
        {
            IntPtr factory = IntPtr.Zero;
            try
            {
                int hr = RoInitialize(RO_INIT_MULTITHREADED);
                bool initialized = hr == 0 || hr == 1;
                try
                {
                    hr = GetFactory("Windows.Graphics.Capture.GraphicsCaptureItem",
                        IID_IGraphicsCaptureItemInterop, out factory);
                    bool ok = hr == 0 && factory != IntPtr.Zero;
                    if (!ok)
                        Log.Debug("Graphics Capture unavailable (hr=0x" +
                            hr.ToString("X8") + "); Explorer will use a frozen copy.");
                    return ok;
                }
                finally
                {
                    if (factory != IntPtr.Zero) Marshal.Release(factory);
                    if (initialized) RoUninitialize();
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Graphics Capture probe failed: " + ex.GetType().Name);
                return false;
            }
        }

        public string LastError { get; private set; }
        public int FrameWidth { get; private set; }
        public int FrameHeight { get; private set; }
        public int ItemWidth { get; private set; }
        public int ItemHeight { get; private set; }

        /// <summary>
        /// Creates the D3D device, the capture item for the window and a frame
        /// pool of the requested size. Must be called on the thread that will
        /// also call TryCopyTo.
        /// </summary>
        public bool Start(IntPtr hwnd, int width, int height)
        {
            LastError = null;
            int hr = RoInitialize(RO_INIT_MULTITHREADED);
            if (hr == 0 || hr == 1) _roInitialized = true;
            else if ((uint)hr != 0x80010106) // RPC_E_CHANGED_MODE: already initialised
            {
                LastError = "RoInitialize hr=0x" + hr.ToString("X8");
                return false;
            }

            int featureLevel;
            hr = D3D11CreateDevice(IntPtr.Zero, D3D_DRIVER_TYPE_HARDWARE, IntPtr.Zero,
                D3D11_CREATE_DEVICE_BGRA_SUPPORT, IntPtr.Zero, 0, D3D11_SDK_VERSION,
                out _d3dDevice, out featureLevel, out _d3dContext);
            if (hr != 0 || _d3dDevice == IntPtr.Zero)
            {
                LastError = "D3D11CreateDevice hr=0x" + hr.ToString("X8");
                return false;
            }

            _createTexture = Vtbl<CreateTexture2DDelegate>(_d3dDevice, SlotCreateTexture2D);
            _copyResource = Vtbl<CopyResourceDelegate>(_d3dContext, SlotCopyResource);
            _map = Vtbl<MapDelegate>(_d3dContext, SlotMap);
            _unmap = Vtbl<UnmapDelegate>(_d3dContext, SlotUnmap);

            Guid iidDxgi = IID_IDXGIDevice;
            IntPtr dxgi;
            hr = Marshal.QueryInterface(_d3dDevice, ref iidDxgi, out dxgi);
            if (hr != 0)
            {
                LastError = "QI IDXGIDevice hr=0x" + hr.ToString("X8");
                return false;
            }
            hr = CreateDirect3D11DeviceFromDXGIDevice(dxgi, out _winrtDevice);
            Marshal.Release(dxgi);
            if (hr != 0 || _winrtDevice == IntPtr.Zero)
            {
                LastError = "CreateDirect3D11DeviceFromDXGIDevice hr=0x" + hr.ToString("X8");
                return false;
            }

            IntPtr itemPtr;
            hr = CreateItem(hwnd, out itemPtr);
            if (hr != 0 || itemPtr == IntPtr.Zero)
            {
                LastError = "CreateForWindow hr=0x" + hr.ToString("X8");
                return false;
            }
            _item = (IGraphicsCaptureItem)Marshal.GetObjectForIUnknown(itemPtr);
            Marshal.Release(itemPtr);

            SizeInt32 itemSize;
            if (_item.get_Size(out itemSize) == 0)
            {
                ItemWidth = itemSize.Width;
                ItemHeight = itemSize.Height;
            }
            if (width < 16 || height < 16)
            {
                width = ItemWidth;
                height = ItemHeight;
            }

            IntPtr poolPtr;
            hr = CreatePool(_winrtDevice, DXGI_FORMAT_B8G8R8A8_UNORM, 2,
                new SizeInt32 { Width = width, Height = height }, out poolPtr);
            if (hr != 0 || poolPtr == IntPtr.Zero)
            {
                LastError = "CreateFreeThreaded hr=0x" + hr.ToString("X8");
                return false;
            }
            _pool = (IDirect3D11CaptureFramePool)Marshal.GetObjectForIUnknown(poolPtr);
            Marshal.Release(poolPtr);

            IntPtr sessionPtr;
            hr = _pool.CreateCaptureSession(_item, out sessionPtr);
            if (hr != 0 || sessionPtr == IntPtr.Zero)
            {
                LastError = "CreateCaptureSession hr=0x" + hr.ToString("X8");
                return false;
            }
            _session = (IGraphicsCaptureSession)Marshal.GetObjectForIUnknown(sessionPtr);
            Marshal.Release(sessionPtr);

            // The yellow capture border is only cosmetic here (the real window
            // sits at the screen edge); ask for it to go away and ignore the
            // refusal that unpackaged apps normally get.
            try
            {
                IGraphicsCaptureSession3 s3 = (IGraphicsCaptureSession3)_session;
                s3.put_IsBorderRequired(false);
            }
            catch
            {
            }

            hr = _session.StartCapture();
            if (hr != 0)
            {
                LastError = "StartCapture hr=0x" + hr.ToString("X8");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Polls the pool for a new frame and copies it into a 32-bit
        /// top-down BGRA buffer. Returns false when no new frame is ready.
        /// </summary>
        public bool TryCopyTo(IntPtr dest, int destW, int destH, int destStride)
        {
            if (_pool == null || dest == IntPtr.Zero || destW <= 0 || destH <= 0) return false;

            IntPtr framePtr;
            int hr = _pool.TryGetNextFrame(out framePtr);
            if (hr != 0)
            {
                LastError = "TryGetNextFrame hr=0x" + hr.ToString("X8");
                return false;
            }
            if (framePtr == IntPtr.Zero) return false;

            object frameObj = Marshal.GetObjectForIUnknown(framePtr);
            Marshal.Release(framePtr);
            IntPtr surfacePtr = IntPtr.Zero;
            IntPtr texturePtr = IntPtr.Zero;
            IDirect3DDxgiInterfaceAccess access = null;
            bool copied = false;
            try
            {
                IDirect3D11CaptureFrame frame = (IDirect3D11CaptureFrame)frameObj;
                SizeInt32 content;
                frame.get_ContentSize(out content);
                if (frame.get_Surface(out surfacePtr) != 0 || surfacePtr == IntPtr.Zero)
                {
                    LastError = "get_Surface failed";
                    return false;
                }

                // GetObjectForIUnknown takes its own reference; without an
                // explicit release the RCW keeps this frame's surface (and its
                // D3D texture) alive until a GC, which leaks a handle per frame.
                access = (IDirect3DDxgiInterfaceAccess)Marshal.GetObjectForIUnknown(surfacePtr);
                Guid iidTex = IID_ID3D11Texture2D;
                if (access.GetInterface(ref iidTex, out texturePtr) != 0 || texturePtr == IntPtr.Zero)
                {
                    LastError = "GetInterface(ID3D11Texture2D) failed";
                    return false;
                }

                Texture2DDesc desc;
                GetDescDelegate getDesc = Vtbl<GetDescDelegate>(texturePtr, SlotGetDesc);
                getDesc(texturePtr, out desc);
                if (desc.Width == 0 || desc.Height == 0)
                {
                    LastError = "texture has zero size";
                    return false;
                }
                FrameWidth = (int)desc.Width;
                FrameHeight = (int)desc.Height;

                if (_staging == IntPtr.Zero || _texW != (int)desc.Width ||
                    _texH != (int)desc.Height || _texFormat != desc.Format)
                {
                    ReleaseStaging();
                    Texture2DDesc sd = new Texture2DDesc();
                    sd.Width = desc.Width;
                    sd.Height = desc.Height;
                    sd.MipLevels = 1;
                    sd.ArraySize = 1;
                    sd.Format = desc.Format;
                    sd.SampleCount = 1;
                    sd.SampleQuality = 0;
                    sd.Usage = D3D11_USAGE_STAGING;
                    sd.BindFlags = 0;
                    sd.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
                    sd.MiscFlags = 0;
                    IntPtr staging;
                    if (_createTexture(_d3dDevice, ref sd, IntPtr.Zero, out staging) != 0 ||
                        staging == IntPtr.Zero)
                    {
                        LastError = "CreateTexture2D(staging) failed";
                        return false;
                    }
                    _staging = staging;
                    _texW = (int)desc.Width;
                    _texH = (int)desc.Height;
                    _texFormat = desc.Format;
                }

                _copyResource(_d3dContext, _staging, texturePtr);

                MappedSubresource mapped;
                if (_map(_d3dContext, _staging, 0, D3D11_MAP_READ, 0, out mapped) != 0)
                {
                    LastError = "Map(staging) failed";
                    return false;
                }
                try
                {
                    copied = CopyMapped(mapped, dest, destW, destH, destStride);
                    if (!copied && LastError == null) LastError = "CopyMapped failed";
                }
                finally
                {
                    _unmap(_d3dContext, _staging, 0);
                }
            }
            catch (Exception ex)
            {
                LastError = ex.GetType().Name + ": " + ex.Message;
            }
            finally
            {
                if (access != null)
                {
                    try { Marshal.ReleaseComObject(access); } catch { }
                    access = null;
                }
                if (texturePtr != IntPtr.Zero) Marshal.Release(texturePtr);
                if (surfacePtr != IntPtr.Zero) Marshal.Release(surfacePtr);
                Marshal.ReleaseComObject(frameObj);
            }
            return copied;
        }

        private bool CopyMapped(MappedSubresource mapped, IntPtr dest, int destW, int destH, int destStride)
        {
            int srcW = _texW;
            int srcH = _texH;
            int pitch = (int)mapped.RowPitch;
            if (pitch < srcW * 4) return false;

            int rowBytes = destW * 4;
            if (srcW == destW && srcH == destH)
            {
                for (int y = 0; y < destH; y++)
                    Native.CopyMemory(IntPtr.Add(dest, y * destStride),
                        IntPtr.Add(mapped.Data, y * pitch), (IntPtr)rowBytes);
                return true;
            }

            // The pool was created with a different size than the frames came
            // back with: nearest-neighbour scale, row by row. The alpha channel
            // is kept: Explorer's Mica backdrop is semi-transparent and the
            // overlay takes those pixels from the opaque takeover frame.
            byte[] src = new byte[srcW * 4];
            byte[] dst = new byte[rowBytes];
            for (int y = 0; y < destH; y++)
            {
                int sy = (int)((long)y * srcH / destH);
                Marshal.Copy(IntPtr.Add(mapped.Data, sy * pitch), src, 0, srcW * 4);
                for (int x = 0; x < destW; x++)
                {
                    int sx = (int)((long)x * srcW / destW) * 4;
                    int o = x * 4;
                    dst[o] = src[sx];
                    dst[o + 1] = src[sx + 1];
                    dst[o + 2] = src[sx + 2];
                    dst[o + 3] = src[sx + 3];
                }
                Marshal.Copy(dst, 0, IntPtr.Add(dest, y * destStride), rowBytes);
            }
            return true;
        }

        private int CreateItem(IntPtr hwnd, out IntPtr item)
        {
            item = IntPtr.Zero;
            IntPtr factory;
            int hr = GetFactory("Windows.Graphics.Capture.GraphicsCaptureItem",
                IID_IGraphicsCaptureItemInterop, out factory);
            if (hr != 0) return hr;
            try
            {
                IGraphicsCaptureItemInterop interop =
                    (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factory);
                try
                {
                    Guid iid = IID_IGraphicsCaptureItem;
                    return interop.CreateForWindow(hwnd, ref iid, out item);
                }
                finally
                {
                    Marshal.ReleaseComObject(interop);
                }
            }
            finally
            {
                Marshal.Release(factory);
            }
        }

        private int CreatePool(IntPtr device, int format, int buffers, SizeInt32 size, out IntPtr pool)
        {
            pool = IntPtr.Zero;
            IntPtr factory;
            int hr = GetFactory("Windows.Graphics.Capture.Direct3D11CaptureFramePool",
                IID_IFramePoolStatics2, out factory);
            if (hr != 0) return hr;
            try
            {
                IDirect3D11CaptureFramePoolStatics2 statics =
                    (IDirect3D11CaptureFramePoolStatics2)Marshal.GetObjectForIUnknown(factory);
                try
                {
                    return statics.CreateFreeThreaded(device, format, buffers, size, out pool);
                }
                finally
                {
                    Marshal.ReleaseComObject(statics);
                }
            }
            finally
            {
                Marshal.Release(factory);
            }
        }

        private static int GetFactory(string className, Guid iid, out IntPtr factory)
        {
            IntPtr hstr;
            int hr = WindowsCreateString(className, className.Length, out hstr);
            if (hr != 0)
            {
                factory = IntPtr.Zero;
                return hr;
            }
            try
            {
                return RoGetActivationFactory(hstr, ref iid, out factory);
            }
            finally
            {
                WindowsDeleteString(hstr);
            }
        }

        /// <summary>
        /// Calls IClosable::Close on a WinRT object held by an RCW. Releasing
        /// the RCW alone leaves a capture session running. IClosable is a
        /// separate interface on the runtime class, so it has to be queried;
        /// its Close is slot 6 (IUnknown + IInspectable).
        /// </summary>
        private static void CloseObject(object rcw)
        {
            if (rcw == null) return;
            IntPtr unk = IntPtr.Zero;
            IntPtr closable = IntPtr.Zero;
            try
            {
                unk = Marshal.GetIUnknownForObject(rcw);
                Guid iid = IID_IClosable;
                if (Marshal.QueryInterface(unk, ref iid, out closable) != 0) return;
                CloseDelegate close = Vtbl<CloseDelegate>(closable, SlotClosableClose);
                close(closable);
            }
            catch
            {
            }
            finally
            {
                if (closable != IntPtr.Zero) Marshal.Release(closable);
                if (unk != IntPtr.Zero) Marshal.Release(unk);
            }
        }

        private void ReleaseStaging()
        {
            if (_staging != IntPtr.Zero)
            {
                Marshal.Release(_staging);
                _staging = IntPtr.Zero;
            }
            _texW = 0;
            _texH = 0;
            _texFormat = 0;
        }

        public void Dispose()
        {
            ReleaseStaging();
            // A WinRT capture session is not stopped by releasing its RCW:
            // IClosable::Close must be called, otherwise the capture pipeline
            // keeps the frame pool and the D3D device alive (threads + handles
            // survive every drag).
            CloseObject(_session);
            CloseObject(_pool);
            if (_session != null)
            {
                try { Marshal.ReleaseComObject(_session); } catch { }
                _session = null;
            }
            if (_pool != null)
            {
                try { Marshal.ReleaseComObject(_pool); } catch { }
                _pool = null;
            }
            if (_item != null)
            {
                try { Marshal.ReleaseComObject(_item); } catch { }
                _item = null;
            }
            if (_winrtDevice != IntPtr.Zero)
            {
                Marshal.Release(_winrtDevice);
                _winrtDevice = IntPtr.Zero;
            }
            if (_d3dContext != IntPtr.Zero)
            {
                Marshal.Release(_d3dContext);
                _d3dContext = IntPtr.Zero;
            }
            if (_d3dDevice != IntPtr.Zero)
            {
                Marshal.Release(_d3dDevice);
                _d3dDevice = IntPtr.Zero;
            }
            if (_roInitialized)
            {
                RoUninitialize();
                _roInitialized = false;
            }
        }

        // ----- interop -------------------------------------------------------

        [StructLayout(LayoutKind.Sequential)]
        private struct SizeInt32
        {
            public int Width;
            public int Height;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Texture2DDesc
        {
            public uint Width;
            public uint Height;
            public uint MipLevels;
            public uint ArraySize;
            public int Format;
            public uint SampleCount;
            public uint SampleQuality;
            public int Usage;
            public uint BindFlags;
            public uint CPUAccessFlags;
            public uint MiscFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MappedSubresource
        {
            public IntPtr Data;
            public uint RowPitch;
            public uint DepthPitch;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateTexture2DDelegate(IntPtr self, ref Texture2DDesc desc,
            IntPtr initialData, out IntPtr texture);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GetDescDelegate(IntPtr self, out Texture2DDesc desc);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void CopyResourceDelegate(IntPtr self, IntPtr dst, IntPtr src);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int MapDelegate(IntPtr self, IntPtr resource, uint subresource,
            int mapType, uint mapFlags, out MappedSubresource mapped);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void UnmapDelegate(IntPtr self, IntPtr resource, uint subresource);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CloseDelegate(IntPtr self);

        private static T Vtbl<T>(IntPtr obj, int slot) where T : class
        {
            IntPtr vtbl = Marshal.ReadIntPtr(obj);
            IntPtr fn = Marshal.ReadIntPtr(vtbl, slot * IntPtr.Size);
            return (T)(object)Marshal.GetDelegateForFunctionPointer(fn, typeof(T));
        }

        // The vtable slots of the D3D11 interfaces involved (verified against
        // d3d11.h): ID3D11Device::CreateTexture2D is 5, ID3D11Texture2D::GetDesc
        // is 10, ID3D11DeviceContext::Map/Unmap/CopyResource are 14/15/47.
        private const int SlotCreateTexture2D = 5;
        private const int SlotGetDesc = 10;
        private const int SlotMap = 14;
        private const int SlotUnmap = 15;
        private const int SlotCopyResource = 47;
        // IClosable sits after IUnknown and IInspectable: Close is slot 6.
        private const int SlotClosableClose = 6;

        [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGraphicsCaptureItemInterop
        {
            int CreateForWindow(IntPtr window, ref Guid iid, out IntPtr result);
            int CreateForMonitor(IntPtr monitor, ref Guid iid, out IntPtr result);
        }

        [ComImport, Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGraphicsCaptureItem
        {
            int GetIids(out int iidCount, out IntPtr iids);
            int GetRuntimeClassName(out IntPtr className);
            int GetTrustLevel(out int trustLevel);
            int get_DisplayName(out IntPtr value);
            int get_Size(out SizeInt32 value);
            int add_Closed(IntPtr handler, out long token);
            int remove_Closed(long token);
        }

        [ComImport, Guid("589b103f-6bbc-5df5-a991-02e28b3b66d5"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDirect3D11CaptureFramePoolStatics2
        {
            int GetIids(out int iidCount, out IntPtr iids);
            int GetRuntimeClassName(out IntPtr className);
            int GetTrustLevel(out int trustLevel);
            int CreateFreeThreaded(IntPtr device, int pixelFormat, int numberOfBuffers,
                SizeInt32 size, out IntPtr result);
        }

        [ComImport, Guid("24eb6d22-1975-422e-82e7-780dbd8ddf24"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDirect3D11CaptureFramePool
        {
            int GetIids(out int iidCount, out IntPtr iids);
            int GetRuntimeClassName(out IntPtr className);
            int GetTrustLevel(out int trustLevel);
            int Recreate(IntPtr device, int pixelFormat, int numberOfBuffers, SizeInt32 size);
            int TryGetNextFrame(out IntPtr result);
            int add_FrameArrived(IntPtr handler, out long token);
            int remove_FrameArrived(long token);
            int CreateCaptureSession(IGraphicsCaptureItem item, out IntPtr result);
        }

        [ComImport, Guid("814e42a9-f70f-4ad7-939b-fddcc6eb880d"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGraphicsCaptureSession
        {
            int GetIids(out int iidCount, out IntPtr iids);
            int GetRuntimeClassName(out IntPtr className);
            int GetTrustLevel(out int trustLevel);
            int StartCapture();
        }

        [ComImport, Guid("f2cdd966-22ae-5ea1-9596-3a289344c3be"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGraphicsCaptureSession3
        {
            int GetIids(out int iidCount, out IntPtr iids);
            int GetRuntimeClassName(out IntPtr className);
            int GetTrustLevel(out int trustLevel);
            int get_IsBorderRequired(out bool value);
            int put_IsBorderRequired(bool value);
        }

        [ComImport, Guid("fa50c623-38da-4b32-acf3-fa9734ad800e"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDirect3D11CaptureFrame
        {
            int GetIids(out int iidCount, out IntPtr iids);
            int GetRuntimeClassName(out IntPtr className);
            int GetTrustLevel(out int trustLevel);
            int get_Surface(out IntPtr value);
            int get_SystemRelativeTime(out long value);
            int get_ContentSize(out SizeInt32 value);
        }

        [ComImport, Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDirect3DDxgiInterfaceAccess
        {
            int GetInterface(ref Guid iid, out IntPtr result);
        }

        [DllImport("combase.dll")]
        private static extern int RoInitialize(int initType);

        [DllImport("combase.dll")]
        private static extern void RoUninitialize();

        [DllImport("combase.dll")]
        private static extern int RoGetActivationFactory(IntPtr activatableClassId,
            ref Guid iid, out IntPtr factory);

        [DllImport("combase.dll", CharSet = CharSet.Unicode)]
        private static extern int WindowsCreateString(string source, int length, out IntPtr hstring);

        [DllImport("combase.dll")]
        private static extern int WindowsDeleteString(IntPtr hstring);

        [DllImport("d3d11.dll")]
        private static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software,
            uint flags, IntPtr featureLevels, uint featureLevelCount, uint sdkVersion,
            out IntPtr device, out int featureLevel, out IntPtr context);

        [DllImport("d3d11.dll")]
        private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice,
            out IntPtr graphicsDevice);
    }
}
