using System.IO;
using System.Text;
using WolfEQ.Services;
using WolfEQ.ViewModels;
using Xunit;

namespace WolfEQ.Tests;

public sealed class UnreadableBindingTests
{
    [Fact]
    public void AnUnreadableSavedDeviceIsLoggedInsteadOfCrashing()
    {
        var path = Path.Combine(DeviceBindingSettings.DataDirectory, "device-binding.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var before = File.Exists(path) ? File.ReadAllBytes(path) : null;
        // Held open, like an antivirus scan. Other tests may hold it for a moment first.
        FileStream? hold = null;
        for (var i = 0; hold is null && i < 50; i++)
        {
            try { hold = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { Thread.Sleep(20); }
        }

        Assert.NotNull(hold);
        try
        {
            var model = new MainViewModel();
            Assert.Contains(model.DeviceLog, line => line.Contains("could not be loaded"));
        }
        finally
        {
            hold.Dispose();
            if (before is null) File.Delete(path);
        }
    }
}
