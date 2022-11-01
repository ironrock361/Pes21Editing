using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Pes21Editing.IO
{
    public static class FileOperations
    {
        public static byte[] Decrypt(string filename)
        {
            if (!File.Exists(filename))
            {
                throw new IOException($"File not found at {filename}");
            }

            var decryptor = System.IO.Path.Combine(".", "decrypter", "decrypter21.exe");
            //var editfile = @"C:\Users\Fatih\Documents\KONAMI\eFootball PES 2021 SEASON UPDATE\292733975847239680\save\EDIT00000000";
            var targetDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(filename), "decrypted");
            var arguments = new List<string> { filename, targetDir };

            try
            {
                var procInfo = new ProcessStartInfo
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    FileName = decryptor,
                    Arguments = string.Join(" ", arguments.Select(a => $"\"{a}\""))
                };

                var process = Process.Start(procInfo);
                process.StandardOutput.ReadToEnd();
                process.WaitForExit();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }

            using (MemoryStream ms = new MemoryStream())
            {
                using (FileStream file = new FileStream(Path.Combine(targetDir, "data.dat"),
                  FileMode.Open, FileAccess.Read))
                {
                    file.CopyTo(ms);
                }
                return ms.ToArray();

            }
        }

        public static void Encrypt(string sourceDir, string targetFilename)
        {
            if (!Directory.Exists(sourceDir))
            {
                throw new IOException($"Directory not found at {sourceDir}");
            }

            var decryptor = System.IO.Path.Combine(".", "decrypter", "encrypter21.exe");
            //var editfile = @"C:\Users\Fatih\Documents\KONAMI\eFootball PES 2021 SEASON UPDATE\292733975847239680\save\EDIT00000000";
            var arguments = new List<string> { sourceDir, targetFilename };

            try
            {
                var procInfo = new ProcessStartInfo
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    FileName = decryptor,
                    Arguments = string.Join(" ", arguments.Select(a => $"\"{a}\""))
                };

                var process = Process.Start(procInfo);
                process.StandardOutput.ReadToEnd();
                process.WaitForExit();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }
    }
}
