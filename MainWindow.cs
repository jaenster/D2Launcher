using System;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;


namespace D2Launcher
{
    public partial class MainWindow : Form
    {
        String ClassicCdKey = "";
        String XpakCdKey = "";
        public MainWindow()
        {
            InitializeComponent();
            GetInstallDir();
            DumpCdKeys();
        }
        void launchButton_Click(object sender, EventArgs e)
        {
            var si = new STARTUP_INFO();
            var args = " -w";
            if (sound.Checked) args += " -ns";
            var success = CreateProcess(installDir.Text + "Game.exe", args, IntPtr.Zero, IntPtr.Zero, false, 4, IntPtr.Zero, installDir.Text, ref si, out PROCESS_INFORMATION pi);
            var d2 = Process.GetProcesses().ToList().FirstOrDefault(p => p.Id == pi.dwProcessId);
            var procHandle = pi.hProcess;// OpenProcess(0x001F0FFF, false, startedProcess.Id);
            var moduleBase = (IntPtr)0x400000; // startedProcess.MainModule.BaseAddress doesn't work in suspended since crt/app isn't loaded yet?
            if (multi.Checked) WriteProcessMemory(procHandle, moduleBase + 0xF562A, new Byte[] { 0xDB }, 1, 0); // replace (test eax, eax) with (test ebx, ebx), same window check
            if (sleepy.Checked) WriteProcessMemory(procHandle, moduleBase + 0x51C31, new Byte[] { 0x90, 0x90 }, 2, 0);
            WriteProcessMemory(procHandle, moduleBase + 0x11FE3B, new Byte[] { 0x90, 0x90, 0x90, 0x90, 0x90, 0x90 }, 6, 0); // remove extrawork
            if (highRes.Checked) ChangeTo1080(procHandle, moduleBase);
            ResumeThread(pi.hThread);
            d2.WaitForInputIdle();
            if (highRes.Checked)
            {
                int style = GetWindowLong(d2.MainWindowHandle, -16);
                SetWindowLong(d2.MainWindowHandle, -16, (style & ~(0x00c00000)));
                SetWindowPos(d2.MainWindowHandle, 0, 0, 0, 1920, 1080, 0);
            }
            if (ClassicCdKey != classicCdKey.Text || XpakCdKey != xpakCdKey.Text) UpdateCdKey(procHandle, moduleBase, classicCdKey.Text, xpakCdKey.Text);
            CloseHandle(procHandle);
        }
        void GetInstallDir()
        {
            installDir.Text = @"C:\Program Files (x86)\Diablo II\";
            if (!File.Exists(installDir.Text + "Game.exe")) installDir.Text = @"C:\Program Files\Diablo II\";
            if (!File.Exists(installDir.Text + "Game.exe")) installDir.Text = Registry.CurrentUser.OpenSubKey("Software\\Blizzard Entertainment\\Diablo II").GetValue("InstallPath").ToString();
            if (!File.Exists(installDir.Text + "Game.exe")) throw new Exception("can't find d2 install");
        }
        void DumpCdKeys()
        {
            var si = new STARTUP_INFO();
            si.dwFlags = 1;
            var success = CreateProcess(installDir.Text + "Game.exe", " -w -ns", IntPtr.Zero, IntPtr.Zero, false, 4, IntPtr.Zero, installDir.Text, ref si, out PROCESS_INFORMATION pi);
            var tempD2 = Process.GetProcesses().ToList().FirstOrDefault(p => p.Id == pi.dwProcessId);
            WriteProcessMemory(tempD2.Handle, (IntPtr)0x400000 + 0xF562A, new Byte[] { 0xDB }, 1, 0); // replace (test eax, eax) with (test ebx, ebx), same window check
            ResumeThread(pi.hThread);
            tempD2.WaitForInputIdle();
            var thread = CreateRemoteThread(tempD2.Handle, IntPtr.Zero, 0, (IntPtr)0x5234D0, IntPtr.Zero, 0, IntPtr.Zero);
            WaitForSingleObject(thread, 5000);
            var moduleBase = (IntPtr)0x400000; // startedProcess.MainModule.BaseAddress doesn't work in suspended since crt/app isn't loaded yet?
            var buffer = new Byte[26];
            ReadProcessMemory(pi.hProcess, moduleBase + 0x482744, buffer, 4, 0);
            ReadProcessMemory(pi.hProcess, (IntPtr)BitConverter.ToInt32(buffer, 0), buffer, 26, 0);
            ClassicCdKey = classicCdKey.Text = Encoding.UTF8.GetString(buffer);
            ReadProcessMemory(pi.hProcess, moduleBase + 0x48274C, buffer, 4, 0);
            ReadProcessMemory(pi.hProcess, (IntPtr)BitConverter.ToInt32(buffer, 0), buffer, 26, 0);
            XpakCdKey = xpakCdKey.Text = Encoding.UTF8.GetString(buffer);
            tempD2.Kill();
        }
        void ChangeTo1080(IntPtr procHandle, IntPtr moduleBase)
        {
            var xResAddrs800 = new Int32[] { 0xf559e, 0x111e05, 0x2c82e0, 0x4BA3C, 0x2c7b39, 0x109c2b, 0x11125b, 0x2b5023, 0x109910 };
            var YResAddrs600 = new Int32[] { 0xF55A4, 0x8e1df, 0x2c7b3e, 0x109c35, 0x111265, 0x2b502d, 0x4ba46 };
            var xRes = 1920;
            var yRes = 1080 - 26;// - System.Windows.Forms.SystemInformation.CaptionHeight - 5;
            foreach (var addr in xResAddrs800) FindAndReplace(procHandle, moduleBase + addr, 0x10, 800, (UInt16)xRes);
            foreach (var addr in YResAddrs600) FindAndReplace(procHandle, moduleBase + addr, 0x10, 600, (UInt16)yRes);

            var bufferXLookUpTableAddr = VirtualAllocEx(procHandle, IntPtr.Zero, 8 * 0x1000, 0x3000, 0x4);
            bufferXLookUpTableAddr += 0x80;
            for (var i = -32; i < 2000; i++) WriteProcessMemory(procHandle, bufferXLookUpTableAddr + i * 4, BitConverter.GetBytes(xRes * i), 4, 0);
            var lutAddrs = new Int32[] { 0xF7F09, 0xF808E, 0xF8219, 0xF8368, 0xF85B3 };
            foreach (var addr in lutAddrs) FindAndReplace(procHandle, moduleBase + addr, 0x10, 0x7C9828, (UInt32)bufferXLookUpTableAddr);

            SideloadBeltTxt(procHandle, moduleBase);
            SideloadInventoryTxt(procHandle, moduleBase);

            WriteProcessMemory(procHandle, moduleBase + 0x56EF8, Enumerable.Range(0, 44).Select(b => (Byte)0x90).ToArray(), 44, 0); // remove default ui offset
            WriteProcessMemory(procHandle, moduleBase + 0x99460, Enumerable.Range(0, 50).Select(b => (Byte)0x90).ToArray(), 50, 0); // remove  draw border
            WriteProcessMemory(procHandle, moduleBase + 0x3A285C, BitConverter.GetBytes(-287), 4, 0); // set y ui offset

            var setCursorPos = new Int32[] { 0x68770 + 0x72, 0x68770 + 0x8d, 0x68840 + 0x7d, 0xfa6b0 + 0x92 };
            foreach (var addr in setCursorPos) WriteProcessMemory(procHandle, moduleBase + addr, new Byte[] { 0x90, 0x90, 0x90, 0x90, 0x90, 0x90 }, 6, 0);
        }
        void SideloadBeltTxt(IntPtr procHandle, IntPtr moduleBase)
        {
            var beltInfo = VirtualAllocEx(procHandle, IntPtr.Zero, 8 * 0x1000, 0x3000, 0x4);
            var beltNumBoxes = new UInt16[] { 12, 8, 4, 16, 8, 12, 16 };
            for (var i = 0; i < 2; i++)
            {
                for (var j = 0; j < 7; j++)
                {
                    var beltTypeOffset = 264 * (j + i * 7);
                    WriteProcessMemory(procHandle, beltInfo + beltTypeOffset + 4, BitConverter.GetBytes(beltNumBoxes[j]), 2, 0);
                    for (var k = 0; k < 4; k++)
                    {
                        for (var l = 0; l < 4; l++)
                        {
                            var size = 29;
                            var x = 1920 / 2 - 470 / 2 + 258 + (size + 2) * l;
                            var y = 1080 - 26 - 9 - size - (size + 2) * k;
                            var ba = beltInfo + beltTypeOffset + 8 + k * 16 + l * 4;
                            WriteProcessMemory(procHandle, beltInfo + beltTypeOffset + 8 + k * 64 + l * 16 + 0x0, BitConverter.GetBytes(x), 4, 0);
                            WriteProcessMemory(procHandle, beltInfo + beltTypeOffset + 8 + k * 64 + l * 16 + 0x4, BitConverter.GetBytes(x + size), 4, 0);
                            WriteProcessMemory(procHandle, beltInfo + beltTypeOffset + 8 + k * 64 + l * 16 + 0x8, BitConverter.GetBytes(y), 4, 0);
                            WriteProcessMemory(procHandle, beltInfo + beltTypeOffset + 8 + k * 64 + l * 16 + 0xc, BitConverter.GetBytes(y + size), 4, 0);
                        }
                    }
                }
            }
            var beltAddrs = new Int32[] { 0x260D1F, 0x260CB6 };
            foreach (var addr in beltAddrs) FindAndReplace(procHandle, moduleBase + addr, 0x10, 0x96D4F8, (UInt32)beltInfo);
            WriteProcessMemory(procHandle, beltInfo, BitConverter.GetBytes(beltInfo.ToInt32()), 4, 0);
        }
        void SideloadInventoryTxt(IntPtr procHandle, IntPtr moduleBase)
        {
            var inventoryInfo = VirtualAllocEx(procHandle, IntPtr.Zero, 8 * 0x1000, 0x3000, 0x4);
            var inventoryInfoPtr = inventoryInfo + 5 * 240 + 12 * 4; // bad hack, using monster/vendor inventory
            var characterInventory = new Box[] {
                new Box(1920-320, 287, 320, 441, 10, 4), // base inv
                new Box(19, 255, 287, 113, 29, 29), // grid
                new Box(20, 47, 55, 112), // rArm
                new Box(133, 77, 56, 82), // torso
                new Box(251, 47, 55, 112), // lArm
                new Box(135, 8, 54, 51), // head
                new Box(209, 35, 23, 24), // beck
                new Box(95, 180, 23, 24), // rHand
                new Box(209, 180, 23, 24), // lHand
                new Box(136, 179, 52, 25), // belt
                new Box(252, 182, 54, 52), // feet
                new Box(21, 181, 54, 53) // gloves
            };
            var bankInventory = new Box[] {
                new Box(0, 287, 321, 222, 6, 8), // base inv
                new Box(74, 82, 170, 231, 29, 29), // grid
            };
            var vendorInventory = new Box[] {
                new Box(0, 287, 321, 442, 10, 10), // base inv
                new Box(16, 63,  289, 289, 29, 29), // grid
            };
            var topTrade = new Box[] {
                new Box(0, 287, 321, 219, 10, 4), // base inv
                new Box(20, 41, 285, 289, 29, 29), // grid
            };
            var bottomTrade = new Box[] {
                new Box(0, 287 + 220, 321, 222, 10, 4), // base inv
                new Box(20, 35, 285, 289, 29, 29), // grid
            };
            var hireling = new Box[] {
                new Box(0, 287, 0, 0), // null
                new Box(0, 0, 0, 0),// null
                characterInventory[2],
                characterInventory[3],
                characterInventory[4],
                characterInventory[5]
            };
            for (var i = 0; i < 32; i++)
            {
                Box[] inventoryData;
                if (i == 5 || i == 5 + 16) inventoryData = vendorInventory;
                else if (i == 12 || i == 12 + 16) inventoryData = bankInventory;
                else if (i == 6 || i == 6 + 16) inventoryData = topTrade;
                else if (i == 7 || i == 7 + 16) inventoryData = bottomTrade;
                else if (i == 13 || i == 13 + 16) inventoryData = hireling;
                else inventoryData = characterInventory;
                var inventoryBytes = inventoryData[0].GetBytes(0, 0).ToList();
                inventoryBytes.AddRange(inventoryData[1].GetBytes(inventoryData[0].Left, inventoryData[0].Top).ToList());
                for (var j = 2; j < inventoryData.Length; j++) inventoryBytes.AddRange(inventoryData[j].GetBytes(inventoryData[0].Left, inventoryData[0].Top));
                WriteProcessMemory(procHandle, inventoryInfo + i * 240, inventoryBytes.ToArray(), inventoryBytes.Count, 0);
            }
            var inventoryAddrs = new Int32[] { 0x25C186, 0x25C1F6, 0x25C276 };
            foreach (var addr in inventoryAddrs) FindAndReplace(procHandle, moduleBase + addr, 0x10, 0x96D4F4, (UInt32)inventoryInfoPtr);
            WriteProcessMemory(procHandle, inventoryInfoPtr, BitConverter.GetBytes(inventoryInfo.ToInt32()), 4, 0);
        }
        void UpdateCdKey(IntPtr procHandle, IntPtr moduleBase, String classic, String xpack)
        {
            var classicCdKeyPtr = VirtualAllocEx(procHandle, IntPtr.Zero, 52, 0x3000, 0x4);
            var xpCdKeyPtr = classicCdKeyPtr + 26;

            WriteProcessMemory(procHandle, moduleBase + 0x11E281, new Byte[] { 0x90, 0x90, 0x90, 0x90, 0x90 }, 5, 0); // remove cdkey read
            WriteProcessMemory(procHandle, moduleBase + 0x121E75, new Byte[] { 0x90, 0x90, 0x90, 0x90, 0x90 }, 5, 0); // remove cdkey read

            WriteProcessMemory(procHandle, classicCdKeyPtr, Encoding.UTF8.GetBytes(classic), 26, 0); // write cdkey into cdkey ptr
            WriteProcessMemory(procHandle, moduleBase + 0x12366C, new Byte[] { 0x90, 0x90, 0x90, 0x90, 0x90 }, 5, 0); // remove writing cdkey ptr (mov dword_882744, eax)
            WriteProcessMemory(procHandle, moduleBase + 0x123530, new Byte[] { 0x90, 0x90, 0x90, 0x90, 0x90 }, 5, 0); // remove clearing/writing cdkey ptr (mov dword_882744, eax)
            //WriteProcessMemory(procHandle, moduleBase + 0x1239CF, new Byte[] { 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90 }, 10, 0); // remove writing cdkey ptr (mov dword_882744, 0)
            WriteProcessMemory(procHandle, moduleBase + 0x1239CF + 6, BitConverter.GetBytes(classicCdKeyPtr.ToInt32()), 4, 0); // remove writing cdkey ptr (mov dword_882744, classicCdKeyPtr), but requires dword_882744 writable
            WriteProcessMemory(procHandle, moduleBase + 0x482744, BitConverter.GetBytes(classicCdKeyPtr.ToInt32()), 4, 0); // writing new cdkey pptr (mov dword_882744, classicCdKeyPtr)
            WriteProcessMemory(procHandle, moduleBase + 0x1234EB, new Byte[] { 0x84 }, 1, 0); // remove error if cdkey is set before game reads (cmp dword_882744, 0)

            WriteProcessMemory(procHandle, moduleBase + 0x123958, new Byte[] { 0x90, 0x90, 0x90, 0x90, 0x90 }, 5, 0); // remove writing cdkey ptr (mov dword_88274C, eax)
            WriteProcessMemory(procHandle, xpCdKeyPtr, Encoding.UTF8.GetBytes(xpack), 26, 0); // write cdkey into cdkey ptr
            WriteProcessMemory(procHandle, moduleBase + 0x48274C, BitConverter.GetBytes(xpCdKeyPtr.ToInt32()), 4, 0); // writing new cdkey pptr (mov dword_88274C, xpCdKeyPtr)
        }
        Boolean FindAndReplace(IntPtr procHandle, IntPtr baseAddr, Int32 searchLength, UInt16 oldVal, UInt16 newVal)
        {
            var b = new Byte[searchLength * 2];
            ReadProcessMemory(procHandle, baseAddr - searchLength, b, searchLength * 2, 0);
            for (var i = 0; i < searchLength * 2 - 1; i++)
            {
                if (BitConverter.ToUInt16(b, i) == oldVal)
                {
                    WriteProcessMemory(procHandle, baseAddr - searchLength + i, BitConverter.GetBytes(newVal), 2, 0);
                    return true;
                }
            }
            return false;
        }
        Boolean FindAndReplace(IntPtr procHandle, IntPtr baseAddr, Int32 searchLength, UInt32 oldVal, UInt32 newVal)
        {
            var b = new Byte[searchLength];
            ReadProcessMemory(procHandle, baseAddr, b, searchLength, 0);
            for (var i = 0; i < searchLength - 3; i++)
            {
                if (BitConverter.ToUInt32(b, i) == oldVal)
                {
                    var q = BitConverter.ToUInt32(b, i);
                    WriteProcessMemory(procHandle, baseAddr + i, BitConverter.GetBytes(newVal), 4, 0);
                    return true;
                }
            }
            return false;
        }
        class Box
        {
            internal Int32 Left;
            internal Int32 Top;
            internal UInt16 Width;
            internal UInt16 Height;
            internal Byte HackX;
            internal Byte HackY;
            internal Box(Int32 l, Int32 t, UInt16 w, UInt16 h, Byte hackX = 0, Byte hackY = 0)
            {
                Left = l;
                Top = t;
                Width = w;
                Height = h;
                HackX = hackX;
                HackY = hackY;
            }
            public Byte[] GetBytes(Int32 offsetX, Int32 offsetY)
            {
                var b = BitConverter.GetBytes(offsetX + Left).ToList();
                b.AddRange(BitConverter.GetBytes(offsetX + Left + Width));
                b.AddRange(BitConverter.GetBytes(offsetY + Top));
                b.AddRange(BitConverter.GetBytes(offsetY + Top + Height));
                if (HackX == 0) b.Add((Byte)Width);
                else b.Add(HackX);
                if (HackY == 0) b.Add((Byte)Height);
                else b.Add(HackY);
                b.Add(0);
                b.Add(0);
                return b.ToArray();
            }
        }
        [DllImport("kernel32.dll")] static extern Boolean CreateProcess(String lpApplicationName, String lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, Boolean bInheritHandles, UInt32 dwCreationFlags, IntPtr lpEnvironment, String lpCurrentDirectory, ref STARTUP_INFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);
        [DllImport("kernel32")] static extern IntPtr OpenProcess(UInt32 dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] Boolean bInheritHandle, Int32 dwProcessId);
        [DllImport("kernel32")] static extern UInt32 ResumeThread(IntPtr hThread);
        [DllImport("kernel32")] static extern UInt32 SuspendThread(IntPtr hThread);
        [DllImport("kernel32")] static extern UInt32 CloseHandle(IntPtr hHandle);
        [DllImport("kernel32")] static extern UInt32 WaitForSingleObject(IntPtr hHandle, UInt32 dwMilliseconds);
        [DllImport("kernel32")] static extern Boolean ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, Byte[] lpBuffer, Int32 nSize, Int32 lpNumberOfBytesRead);
        [DllImport("kernel32")] static extern Boolean WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, Byte[] lpBuffer, Int32 nSize, Int32 lpNumberOfBytesWritten);
        [DllImport("kernel32")] static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, Int32 dwSize, Int32 flAllocationType, Int32 flProtect);
        [DllImport("user32")] static extern Int32 SetWindowLong(IntPtr hWnd, Int32 nIndex, Int32 dwNewLong);
        [DllImport("user32")] static extern Int32 GetWindowLong(IntPtr hWnd, Int32 nIndex);
        [DllImport("user32")] static extern Boolean SetWindowPos(IntPtr hwnd, Int32 hWndInsertAfter, Int32 x, Int32 Y, Int32 cx, Int32 cy, Int32 wFlags);
        [DllImport("kernel32")] static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, UInt32 dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, UInt32 dwCreationFlags, IntPtr lpThreadId);
        struct STARTUP_INFO
        {
            internal UInt32 cb;
            internal String lpReserved;
            internal String lpDesktop;
            internal String lpTitle;
            internal UInt32 dwX;
            internal UInt32 dwY;
            internal UInt32 dwXSize;
            internal UInt32 dwYSize;
            internal UInt32 dwXCountChars;
            internal UInt32 dwYCountChars;
            internal UInt32 dwFillAttribute;
            internal UInt32 dwFlags;
            internal Int16 wShowWindow;
            internal Int16 cbReserved2;
            internal IntPtr lpReserved2;
            internal IntPtr hStdInput;
            internal IntPtr hStdOutput;
            internal IntPtr hStdError;
        }
        struct PROCESS_INFORMATION
        {
            internal IntPtr hProcess;
            internal IntPtr hThread;
            internal UInt32 dwProcessId;
            internal UInt32 dwThreadId;
        }
    }
}
