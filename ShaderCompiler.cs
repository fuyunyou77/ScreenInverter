using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ScreenInverter;

public static class ShaderCompiler
{
    [DllImport("d3dcompiler_47.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private static extern int D3DCompile(
        IntPtr pSrcData,
        IntPtr SrcDataSize,
        [MarshalAs(UnmanagedType.LPStr)] string pSourceName,
        IntPtr pDefines,
        IntPtr pInclude,
        [MarshalAs(UnmanagedType.LPStr)] string pEntryPoint,
        [MarshalAs(UnmanagedType.LPStr)] string pTarget,
        uint Flags1,
        uint Flags2,
        out IntPtr ppCode,
        out IntPtr ppErrorMsgs);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr ptr);

    public static byte[] CompileHlsl(string hlslSource, string entryPoint = "main", string profile = "ps_2_0")
    {
        byte[] sourceBytes = Encoding.ASCII.GetBytes(hlslSource);
        IntPtr pSource = Marshal.AllocHGlobal(sourceBytes.Length);
        Marshal.Copy(sourceBytes, 0, pSource, sourceBytes.Length);

        try
        {
            int hr = D3DCompile(
                pSource,
                (IntPtr)sourceBytes.Length,
                "Shader",
                IntPtr.Zero,
                IntPtr.Zero,
                entryPoint,
                profile,
                0, // No flags
                0,
                out IntPtr pCodeBlob,
                out IntPtr pErrorBlob);

            if (hr < 0)
            {
                if (pErrorBlob != IntPtr.Zero)
                {
                    string error = GetStringFromBlob(pErrorBlob);
                    ReleaseBlob(pErrorBlob);
                    throw new Exception("HLSL Error: " + error);
                }
                throw new Exception($"D3DCompile failed with 0x{hr:X}");
            }

            if (pCodeBlob == IntPtr.Zero) 
                throw new Exception("Success HRESULT but Null Code Blob.");

            byte[] bytecode = GetBytesFromBlob(pCodeBlob);
            ReleaseBlob(pCodeBlob);
            return bytecode;
        }
        finally
        {
            Marshal.FreeHGlobal(pSource);
        }
    }

    private static string GetStringFromBlob(IntPtr pBlob)
    {
        IntPtr vtable = Marshal.ReadIntPtr(pBlob);
        IntPtr getBufferPtr = Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size);
        IntPtr getBufferSizePtr = Marshal.ReadIntPtr(vtable, 4 * IntPtr.Size);

        // Call ID3DBlob::GetBufferPointer
        var getBuffer = Marshal.GetDelegateForFunctionPointer<GetBufferDelegate>(getBufferPtr);
        IntPtr buffer = getBuffer(pBlob);

        // Call ID3DBlob::GetBufferSize
        var getBufferSize = Marshal.GetDelegateForFunctionPointer<GetBufferSizeDelegate>(getBufferSizePtr);
        IntPtr size = getBufferSize(pBlob);

        if (buffer == IntPtr.Zero || size == IntPtr.Zero) return "Unknown Error (Null Blob)";
        
        byte[] data = new byte[(int)size];
        Marshal.Copy(buffer, data, 0, (int)size);
        return Encoding.ASCII.GetString(data);
    }

    private static byte[] GetBytesFromBlob(IntPtr pBlob)
    {
        IntPtr vtable = Marshal.ReadIntPtr(pBlob);
        IntPtr getBufferPtr = Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size);
        IntPtr getBufferSizePtr = Marshal.ReadIntPtr(vtable, 4 * IntPtr.Size);

        var getBuffer = Marshal.GetDelegateForFunctionPointer<GetBufferDelegate>(getBufferPtr);
        IntPtr buffer = getBuffer(pBlob);

        var getBufferSize = Marshal.GetDelegateForFunctionPointer<GetBufferSizeDelegate>(getBufferSizePtr);
        IntPtr size = getBufferSize(pBlob);

        if (buffer == IntPtr.Zero) throw new Exception("Blob buffer is null");
        
        byte[] data = new byte[(int)size];
        Marshal.Copy(buffer, data, 0, (int)size);
        return data;
    }

    private static void ReleaseBlob(IntPtr pBlob)
    {
        IntPtr vtable = Marshal.ReadIntPtr(pBlob);
        IntPtr releasePtr = Marshal.ReadIntPtr(vtable, 2 * IntPtr.Size); // IUknown::Release
        var release = Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(releasePtr);
        release(pBlob);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr GetBufferDelegate(IntPtr thisPtr);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr GetBufferSizeDelegate(IntPtr thisPtr);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint ReleaseDelegate(IntPtr thisPtr);
}
