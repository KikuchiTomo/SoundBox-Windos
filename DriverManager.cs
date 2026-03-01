using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace SoundBox
{
    public static class DriverManager
    {
        // Detect virtual audio cables (VB-Cable, VoiceMeeter, etc.)
        public static bool IsInstalled()
        {
            return FindVirtualCable() != null;
        }

        public static string? FindVirtualCable()
        {
            try
            {
                var enumerator = new MMDeviceEnumerator();
                var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                foreach (var d in devices)
                {
                    string name = d.FriendlyName.ToLowerInvariant();
                    if (name.Contains("cable") || name.Contains("virtual") ||
                        name.Contains("voicemeeter") || name.Contains("vb-audio"))
                        return d.FriendlyName;
                }
            }
            catch { }
            return null;
        }

        public static string GetStatusText()
        {
            string? cable = FindVirtualCable();
            if (cable != null)
                return $"Virtual Cable: {cable}";
            return "No virtual audio cable detected";
        }

        public static bool IsAdmin()
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }

        public static (bool Success, string Message) Install()
        {
            // Check if VB-Cable is already installed
            string? cable = FindVirtualCable();
            if (cable != null)
                return (true, $"Virtual audio cable already available: {cable}\n\nSelect it as output device to route audio.");

            return (false,
                "No virtual audio cable found.\n\n" +
                "To use SoundBox as a virtual microphone, install one of these free tools:\n\n" +
                "1. VB-Audio Virtual Cable (recommended)\n" +
                "   https://vb-audio.com/Cable/\n\n" +
                "2. VoiceMeeter\n" +
                "   https://vb-audio.com/Voicemeeter/\n\n" +
                "After installing, restart SoundBox and select the virtual cable as output device.");
        }

        public static void OpenVBCableDownload()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://vb-audio.com/Cable/",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        // Keep for backward compatibility but no longer used
        public static void RestartAsAdmin() { }
        public static (bool Success, string Message) Uninstall() => (true, "No driver to uninstall. Virtual audio cables are managed separately.");
    }
}
