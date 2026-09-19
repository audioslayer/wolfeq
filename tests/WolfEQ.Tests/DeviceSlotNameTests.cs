using System.IO;
using WolfEQ.Services;
using WolfEQ.ViewModels;
using Xunit;

namespace WolfEQ.Tests;

public sealed class DeviceSlotNameTests
{
    [Fact]
    public void NamesPersistPerPhysicalDeviceModelAndSlot()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"wolfeq-slots-{Guid.NewGuid()}");
        try
        {
            var service = new DeviceSlotNameService(directory);
            Assert.Empty(service.Load(null, "k13"));
            service.Save("USB-A", "k13", 5, "  Evening music  ");
            service.Save("USB-A", "k13", 6, "Gaming");
            service.Save("USB-B", "k13", 5, "Second device");
            service.Save("USB-A", "ka17", 5, "Other model");
            var reloaded = new DeviceSlotNameService(directory);
            Assert.Equal("Evening music", reloaded.Load("usb-a", "k13")[5]);
            Assert.Equal("Gaming", reloaded.Load("USB-A", "k13")[6]);
            Assert.Equal("Second device", reloaded.Load("USB-B", "k13")[5]);
            Assert.Equal("Other model", reloaded.Load("USB-A", "ka17")[5]);
            service.Save("USB-A", "k13", 5, "Replacement");
            Assert.Equal("Replacement", reloaded.Load("USB-A", "k13")[5]);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void LocalNameIsSeparateFromDeviceNameAndSurvivesReadbackRename()
    {
        var slot = new DeviceUserPresetOption(1, 5, "USER 1", "Evening music");
        var readback = slot with { Name = "USER 1" };
        Assert.Equal("USER 1", readback.SlotLabel);
        Assert.Equal("Evening music", readback.LocalEqSubtitle);
        Assert.Equal("No local name yet", new DeviceUserPresetOption(2, 6).LocalEqSubtitle);
    }
}
