// Form1.cs
using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Windows.Forms;
using Microsoft.CSharp;

namespace DllInjectorCreator
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void btnCreate_Click(object sender, EventArgs e)
        {
            string targetExeName = txtTargetExeName.Text.Trim();
            if (string.IsNullOrEmpty(targetExeName))
            {
                MessageBox.Show("Hedef exe ismi girin!", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Injector template'ini al (aşağıda verilen 2. kısım)
            string injectorTemplate = GetInjectorTemplate();

            // Placeholder'ı replace et
            string injectorCode = injectorTemplate.Replace("{TARGET_EXE_NAME}", targetExeName);

            // Derleme ayarları
            CSharpCodeProvider provider = new CSharpCodeProvider();
            CompilerParameters parameters = new CompilerParameters
            {
                GenerateExecutable = true,
                OutputAssembly = $"Injector {targetExeName}",
                CompilerOptions = "/target:winexe" // Windows Forms için konsol yerine windowed exe
            };

            // Gerekli referanslar (Windows Forms ve System için)
            parameters.ReferencedAssemblies.Add("System.dll");
            parameters.ReferencedAssemblies.Add("System.Windows.Forms.dll");
            parameters.ReferencedAssemblies.Add("System.Drawing.dll");

            // Kodu derle
            CompilerResults results = provider.CompileAssemblyFromSource(parameters, injectorCode);

            if (results.Errors.HasErrors)
            {
                string errorMsg = "Derleme hatası:\n";
                foreach (CompilerError error in results.Errors)
                {
                    errorMsg += error.ErrorText + "\n";
                }
                MessageBox.Show(errorMsg, "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else
            {
                MessageBox.Show($"Injector başarıyla oluşturuldu: {parameters.OutputAssembly}", "Başarılı", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        // Injector template'ini string olarak döndür (bu, oluşturulan injector'ün kaynak kodu)
        private string GetInjectorTemplate()
        {
            return @"
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DllInjector
{
    class Program
    {
        // P/Invoke tanımları
        [DllImport(""kernel32.dll"", SetLastError = true)]
        static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport(""kernel32.dll"", SetLastError = true, ExactSpelling = true)]
        static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

        [DllImport(""kernel32.dll"", SetLastError = true)]
        static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out UIntPtr lpNumberOfBytesWritten);

        [DllImport(""kernel32.dll"")]
        static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport(""kernel32.dll"", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport(""kernel32.dll"", SetLastError = true)]
        static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, out IntPtr lpThreadId);

        [DllImport(""kernel32.dll"", SetLastError = true)]
        static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport(""kernel32.dll"", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool CloseHandle(IntPtr hObject);

        // Sabitler
        const uint PROCESS_ALL_ACCESS = 0x001F0FFF;
        const uint MEM_COMMIT = 0x00001000;
        const uint MEM_RESERVE = 0x00002000;
        const uint PAGE_READWRITE = 4;

        [STAThread]
        static void Main()
        {
            string targetExe = ""{TARGET_EXE_NAME}"";
            Process[] processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(targetExe));

            if (processes.Length == 0)
            {
                MessageBox.Show(""Hedef uygulama açık değil!"", ""Hata"", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // İlk süreci al (veya en yenisini, ama basitlik için ilk)
            Process targetProcess = processes[0];

            // OpenFileDialog ile DLL seç
            OpenFileDialog ofd = new OpenFileDialog
            {
                Filter = ""DLL Files (*.dll)|*.dll"",
                Title = ""DLL Dosyası Seçin""
            };

            if (ofd.ShowDialog() != DialogResult.OK)
            {
                MessageBox.Show(""DLL seçilmedi!"", ""Hata"", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string dllPath = ofd.FileName;

            // Injection işlemi
            try
            {
                IntPtr hProcess = OpenProcess(PROCESS_ALL_ACCESS, false, targetProcess.Id);
                if (hProcess == IntPtr.Zero)
                {
                    throw new Exception(""Süreci açma hatası!"");
                }

                byte[] dllPathBytes = System.Text.Encoding.ASCII.GetBytes(dllPath + ""\0"");
                uint size = (uint)dllPathBytes.Length;

                IntPtr allocMem = VirtualAllocEx(hProcess, IntPtr.Zero, size, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
                if (allocMem == IntPtr.Zero)
                {
                    throw new Exception(""Bellek ayırma hatası!"");
                }

                UIntPtr bytesWritten;
                bool success = WriteProcessMemory(hProcess, allocMem, dllPathBytes, size, out bytesWritten);
                if (!success || bytesWritten.ToUInt32() != size)
                {
                    throw new Exception(""Bellek yazma hatası!"");
                }

                IntPtr kernel32 = GetModuleHandle(""Kernel32"");
                IntPtr loadLibrary = GetProcAddress(kernel32, ""LoadLibraryA"");
                if (loadLibrary == IntPtr.Zero)
                {
                    throw new Exception(""LoadLibraryA adresi alınamadı!"");
                }

                IntPtr threadId;
                IntPtr hThread = CreateRemoteThread(hProcess, IntPtr.Zero, 0, loadLibrary, allocMem, 0, out threadId);
                if (hThread == IntPtr.Zero)
                {
                    throw new Exception(""Uzak thread oluşturma hatası!"");
                }

                WaitForSingleObject(hThread, 0xFFFFFFFF); // Sonsuz bekle

                CloseHandle(hThread);
                CloseHandle(hProcess);

                MessageBox.Show(""DLL başarıyla enjekte edildi!"", ""Başarılı"", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(""Injection hatası: "" + ex.Message, ""Hata"", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}";
        }
    }
}