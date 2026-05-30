using System.Runtime.InteropServices;
namespace FlagInjector;
internal static unsafe class Obf
{
    static byte K(int i, int n) => (byte)(0x5F ^ ((i * 0x17) & 0xFF) ^ ((n * 0x2B) & 0xFF));
    static string Dec(byte[] e)
    {
        var b = new char[e.Length];
        for (int i = 0; i < e.Length; i++) b[i] = (char)(e[i] ^ K(i, e.Length));
        return new string(b);
    }
    static readonly byte[] _PROC         = { 0xbd, 0x97, 0xa3, 0xc6, 0xdc, 0xe4, 0x35, 0x22, 0x36, 0x59, 0x6c, 0x60, 0xb9, 0xa1, 0xd9, 0xd7 }; 
    static readonly byte[] _NTDLL        = { 0xb2, 0xbf, 0x96, 0xf5, 0xec, 0x81, 0x32, 0x11, 0x08 };                                             
    static readonly byte[] _K32          = { 0x30, 0x29, 0x07, 0x70, 0x62, 0x44, 0xe2, 0xc8, 0xcd, 0xf0, 0xd1, 0xca };                          
    static readonly byte[] _ADVAPI       = { 0x3a, 0x28, 0x03, 0x7f, 0x77, 0x41, 0xe2, 0xc8, 0xcd, 0xf0, 0xd1, 0xca };                          
    static readonly byte[] _NTWRITE      = { 0x4d, 0x60, 0x7a, 0x34, 0x36, 0x04, 0xec, 0xf4, 0xd2, 0xbe, 0x91, 0x8b, 0x76, 0x44, 0x0c, 0x3f, 0x1e, 0xeb, 0xef, 0xcf }; 
    static readonly byte[] _NTREAD       = { 0x20, 0x0d, 0x12, 0x4e, 0x53, 0x79, 0xb2, 0xa6, 0xa4, 0xd5, 0xfd, 0xf2, 0x16, 0x08, 0x49, 0x5a, 0x71, 0x9b, 0x89 };       
    static readonly byte[] _NTSUSPEND    = { 0x94, 0xb9, 0xa7, 0xea, 0xf5, 0xd9, 0x35, 0x15, 0x06, 0x41, 0x54, 0x55, 0xab, 0x90, 0xfc };                               
    static readonly byte[] _NTRESUME     = { 0x4b, 0x66, 0x79, 0x25, 0x2a, 0x03, 0xe2, 0xc1, 0xe9, 0xa2, 0x91, 0x9d, 0x70, 0x4a };                                    
    static readonly byte[] _NTOPEN       = { 0x3e, 0x13, 0x11, 0x45, 0x49, 0x6d, 0xaa, 0xa3, 0xa7, 0xdc, 0xf3, 0xfe, 0x17 };                                          
    static readonly byte[] _NTOPENTHRD   = { 0x15, 0x38, 0x3a, 0x6e, 0x62, 0x46, 0x85, 0x92, 0x91, 0xf1, 0xdc, 0xc2 };                                               
    static readonly byte[] _NTQSI        = { 0x19, 0x34, 0x28, 0x67, 0x6e, 0x56, 0xa4, 0xa5, 0x96, 0xeb, 0xc5, 0xcf, 0x2e, 0x35, 0x7b, 0x68, 0x48, 0xa2, 0xa4, 0x83, 0xef, 0xdd, 0xc2, 0x28 }; 
    static readonly byte[] _PROTECT      = { 0xb9, 0x91, 0xb3, 0xde, 0xc6, 0xfd, 0x09, 0x1e, 0x25, 0x4f, 0x7d, 0x77, 0x98, 0xb0, 0xe8, 0xce };                       
    static readonly byte[] _QUERYEX      = { 0x53, 0x7b, 0x59, 0x34, 0x2c, 0x17, 0xe3, 0xf5, 0xc8, 0xaf, 0x91, 0x81, 0x54, 0x56 };                                    
    static readonly byte[] _CLOSE        = { 0xc5, 0xfd, 0xc7, 0xb0, 0xbf, 0xbd, 0x6d, 0x49, 0x5a, 0x25, 0x05 };                                                     
    static readonly byte[] _GETTIMES     = { 0x9d, 0xa8, 0x80, 0xcf, 0xf4, 0xc6, 0x33, 0x1e, 0x11, 0x66, 0x68, 0x4e, 0xa3, 0x94, 0xeb };                             
    static readonly byte[] _VALLOC       = { 0x0d, 0x25, 0x07, 0x6a, 0x72, 0x49, 0xbd, 0xbb, 0x8f, 0xf8, 0xd2, 0xc5 };                                                
    static readonly byte[] _GETMOD       = { 0xa8, 0x9d, 0xb5, 0xe7, 0xdc, 0xf8, 0x10, 0x22, 0x32, 0x68, 0x68, 0x7c, 0x9f, 0xa8, 0xc8, 0xf7 };                      
    static readonly byte[] _GETPROC      = { 0x42, 0x77, 0x5f, 0x10, 0x2b, 0x19, 0xec, 0xe5, 0xd9, 0xae, 0x91, 0x9d, 0x62, 0x5d };                                   
    static readonly byte[] _OPENTOKEN    = { 0xa0, 0x88, 0xa4, 0xc4, 0xe3, 0xee, 0x0a, 0x2d, 0x32, 0x53, 0x7a, 0x46, 0x94, 0xaf, 0xc8, 0xd8 };                      
    static readonly byte[] _GETTOKENINFO = { 0x29, 0x1c, 0x34, 0x7f, 0x5d, 0x76, 0x81, 0xa1, 0x9f, 0xcf, 0xee, 0xfc, 0x08, 0x28, 0x4d, 0x43, 0x77, 0x86, 0x9e };   
    public static string ProcName => Dec(_PROC);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandleA(IntPtr name);
    [DllImport("kernel32.dll")] static extern IntPtr GetProcAddress(IntPtr mod, IntPtr name);
    [DllImport("kernel32.dll")] static extern IntPtr VirtualAlloc(IntPtr addr, uint sz, uint type, uint prot);
    static IntPtr GetMod(byte[] enc)
    {
        var s = Dec(enc);
        var p = Marshal.StringToHGlobalAnsi(s);
        try { return GetModuleHandleA(p); }
        finally { Marshal.FreeHGlobal(p); }
    }
    static IntPtr GetFn(IntPtr mod, byte[] enc)
    {
        var s = Dec(enc);
        var p = Marshal.StringToHGlobalAnsi(s);
        try { return GetProcAddress(mod, p); }
        finally { Marshal.FreeHGlobal(p); }
    }
    static IntPtr _ntdll;
    static IntPtr _k32;
    static IntPtr _advapi;
    static IntPtr Ntdll  => _ntdll  != IntPtr.Zero ? _ntdll  : (_ntdll  = GetMod(_NTDLL));
    static IntPtr K32    => _k32    != IntPtr.Zero ? _k32    : (_k32    = GetMod(_K32));
    static IntPtr Advapi => _advapi != IntPtr.Zero ? _advapi : (_advapi = GetMod(_ADVAPI));
    delegate int  DNtWrite  (IntPtr h, IntPtr addr, byte[] buf, uint sz, out uint written);
    delegate int  DNtRead   (IntPtr h, IntPtr addr, byte[] buf, uint sz, out uint read);
    delegate int  DNtSusp   (IntPtr h, out uint prev);
    delegate int  DNtResume (IntPtr h, out uint prev);
    delegate int  DNtOpen   (out IntPtr h, uint access, ref ObjAttr oa, ref ClientId cid);
    delegate int  DNtOpenTh (out IntPtr h, uint access, ref ObjAttr oa, ref ClientId cid);
    delegate int  DNtQsi    (int cls, IntPtr buf, uint len, out uint ret);
    delegate bool DProtect  (IntPtr h, IntPtr addr, nint sz, uint np, out uint op);
    delegate bool DQuery    (IntPtr h, IntPtr addr, out MBI mbi, uint len);
    delegate bool DClose    (IntPtr h);
    delegate bool DGetTimes (IntPtr h, out long cr, out long ex, out long kr, out long usr);
    delegate bool DOpenTok  (IntPtr h, uint acc, out IntPtr tok);
    delegate bool DGetTokInf(IntPtr tok, int cls, IntPtr buf, uint len, out uint ret);
    static DNtWrite?   _write;
    static DNtRead?    _read;
    static DNtSusp?    _susp;
    static DNtResume?  _resume;
    static DNtOpen?    _open;
    static DNtOpenTh?  _openTh;
    static DNtQsi?     _qsi;
    static DProtect?   _protect;
    static DQuery?     _query;
    static DClose?     _close;
    static DGetTimes?  _getTimes;
    static DOpenTok?   _openTok;
    static DGetTokInf? _getTokInf;
    static int ReadSsn(IntPtr fn)
    {
        if (fn == IntPtr.Zero) return -1;
        var stub = new byte[8];
        Marshal.Copy(fn, stub, 0, 8);
        if (stub[3] == 0xB8) return BitConverter.ToInt32(stub, 4);
        return -1;
    }
    static T EmitSyscallStub<T>(byte[] encName) where T : Delegate
    {
        var fn  = GetFn(Ntdll, encName);
        int ssn = ReadSsn(fn);
        if (ssn >= 0)
        {
            var mem = VirtualAlloc(IntPtr.Zero, 16, 0x3000, 0x40);
            if (mem != IntPtr.Zero)
            {
                var code = new byte[] {
                    0x4C, 0x8B, 0xD1,
                    0xB8, (byte)ssn, (byte)(ssn >> 8), (byte)(ssn >> 16), (byte)(ssn >> 24),
                    0x0F, 0x05,
                    0xC3
                };
                Marshal.Copy(code, 0, mem, code.Length);
                return Marshal.GetDelegateForFunctionPointer<T>(mem);
            }
        }
        return Marshal.GetDelegateForFunctionPointer<T>(fn);
    }
    static T SC<T>(ref T? cached, byte[] encName) where T : Delegate
        => cached ??= EmitSyscallStub<T>(encName);
    static T Rt<T>(ref T? cached, IntPtr mod, byte[] encName) where T : Delegate
    {
        if (cached is not null) return cached;
        var ptr = GetFn(mod, encName);
        if (ptr == IntPtr.Zero) throw new EntryPointNotFoundException(Dec(encName));
        return cached = Marshal.GetDelegateForFunctionPointer<T>(ptr);
    }
    public static int NtWrite(IntPtr h, IntPtr addr, byte[] buf, uint sz, out uint wr) =>
        SC(ref _write, _NTWRITE)(h, addr, buf, sz, out wr);
    public static int NtRead(IntPtr h, IntPtr addr, byte[] buf, uint sz, out uint rd) =>
        SC(ref _read, _NTREAD)(h, addr, buf, sz, out rd);
    public static int NtSuspendThread(IntPtr h, out uint prev) =>
        SC(ref _susp, _NTSUSPEND)(h, out prev);
    public static int NtResumeThread(IntPtr h, out uint prev) =>
        SC(ref _resume, _NTRESUME)(h, out prev);
    public static IntPtr NtOpenProcess(int pid, uint access)
    {
        var cid = new ClientId { UniqueProcess = (IntPtr)pid };
        var oa  = new ObjAttr  { Length = Marshal.SizeOf<ObjAttr>() };
        return SC(ref _open, _NTOPEN)(out IntPtr h, access, ref oa, ref cid) == 0 ? h : IntPtr.Zero;
    }
    public static IntPtr NtOpenThread(int tid)
    {
        const uint THREAD_SUSPEND_RESUME = 0x0002;
        var cid = new ClientId { UniqueThread = (IntPtr)tid };
        var oa  = new ObjAttr  { Length = Marshal.SizeOf<ObjAttr>() };
        return SC(ref _openTh, _NTOPENTHRD)(out IntPtr h, THREAD_SUSPEND_RESUME, ref oa, ref cid) == 0
            ? h : IntPtr.Zero;
    }
    public static List<int> GetThreadIds(int targetPid)
    {
        var result = new List<int>();
        uint size  = 0x40000; 
        IntPtr buf = IntPtr.Zero;
        try
        {
            for (int tries = 0; tries < 4; tries++)
            {
                buf = Marshal.AllocHGlobal((int)size);
                int st = SC(ref _qsi, _NTQSI)(5 , buf, size, out uint needed);
                if (st == 0) break;          
                if (st == unchecked((int)0xC0000004)) { size = needed + 0x1000; Marshal.FreeHGlobal(buf); buf = IntPtr.Zero; continue; } 
                return result;
            }
            if (buf == IntPtr.Zero) return result;
            nint ptr = (nint)buf;
            while (true)
            {
                uint next = (uint)Marshal.ReadInt32((IntPtr)ptr, 0);
                int  nThr = Marshal.ReadInt32((IntPtr)ptr, 4);
                long pid  = Marshal.ReadInt64((IntPtr)ptr, 0x50);
                if (pid == targetPid)
                {
                    nint thrPtr = ptr + 0x100;
                    for (int i = 0; i < nThr; i++, thrPtr += 0x50)
                        result.Add((int)Marshal.ReadInt64((IntPtr)thrPtr, 0x30));
                    break;
                }
                if (next == 0) break;
                ptr += (nint)next;
            }
        }
        catch { }
        finally { if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf); }
        return result;
    }
    public static bool VirtualProtectEx(IntPtr h, IntPtr addr, nint sz, uint np, out uint op) =>
        Rt(ref _protect, K32, _PROTECT)(h, addr, sz, np, out op);
    public static bool VirtualQueryEx(IntPtr h, IntPtr addr, out MBI mbi, uint len) =>
        Rt(ref _query, K32, _QUERYEX)(h, addr, out mbi, len);
    public static bool CloseHandle(IntPtr h) =>
        Rt(ref _close, K32, _CLOSE)(h);
    public static bool GetProcessTimes(IntPtr h, out long cr, out long ex, out long kr, out long usr) =>
        Rt(ref _getTimes, K32, _GETTIMES)(h, out cr, out ex, out kr, out usr);
    public static bool OpenProcessToken(IntPtr h, uint acc, out IntPtr tok) =>
        Rt(ref _openTok, Advapi, _OPENTOKEN)(h, acc, out tok);
    public static bool GetTokenInformation(IntPtr tok, int cls, IntPtr buf, uint len, out uint ret) =>
        Rt(ref _getTokInf, Advapi, _GETTOKENINFO)(tok, cls, buf, len, out ret);
    [StructLayout(LayoutKind.Sequential)]
    public struct ClientId
    {
        public IntPtr UniqueProcess;
        public IntPtr UniqueThread;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct ObjAttr
    {
        public int    Length;
        public IntPtr RootDirectory;
        public IntPtr ObjectName;
        public uint   Attributes;
        public IntPtr SecurityDescriptor;
        public IntPtr SecurityQualityOfService;
    }
}
