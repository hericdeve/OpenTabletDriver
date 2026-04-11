using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NSubstitute;
using OpenTabletDriver.Plugin.Components;
using OpenTabletDriver.Plugin.Devices;
using OpenTabletDriver.Plugin.Tablet;
using Xunit;

namespace OpenTabletDriver.Tests
{
    public class DriverAuxiliaryDeviceDetectionTest
    {
        [Fact]
        public void Detect_AttachesAllMatchingAuxiliaryDevices()
        {
            var parser = Substitute.For<IReportParser<IDeviceReport>>();
            parser.Parse(Arg.Any<byte[]>()).Returns(call => new DeviceReport(call.Arg<byte[]>()));

            var parserProvider = Substitute.For<IReportParserProvider>();
            parserProvider.GetReportParser(Arg.Any<string>()).Returns(parser);

            var configurationProvider = Substitute.For<IDeviceConfigurationProvider>();
            configurationProvider.TabletConfigurations.Returns([CreateTabletConfiguration()]);

            var hub = new StubCompositeDeviceHub(
            [
                CreateEndpoint("digitizer-1"),
                CreateEndpoint("aux-1"),
                CreateEndpoint("aux-2")
            ]);

            using var driver = new Driver(hub, parserProvider, configurationProvider);

            Assert.True(driver.Detect());

            var detectedTablet = Assert.Single(driver.InputDevices);
            Assert.Equal(3, detectedTablet.InputDevices.Count);
            Assert.Equal(
                ["aux-1", "aux-2", "digitizer-1"],
                detectedTablet.InputDevices.Select(device => device.Endpoint.DevicePath).OrderBy(path => path).ToArray());
        }

        [Fact]
        public void Detect_DoesNotAttachAuxiliaryDeviceOnDigitizerPath()
        {
            var parser = Substitute.For<IReportParser<IDeviceReport>>();
            parser.Parse(Arg.Any<byte[]>()).Returns(call => new DeviceReport(call.Arg<byte[]>()));

            var parserProvider = Substitute.For<IReportParserProvider>();
            parserProvider.GetReportParser(Arg.Any<string>()).Returns(parser);

            var configurationProvider = Substitute.For<IDeviceConfigurationProvider>();
            configurationProvider.TabletConfigurations.Returns([CreateTabletConfigurationWithOverlappingAuxiliaryPath()]);

            var hub = new StubCompositeDeviceHub(
            [
                CreateEndpoint("shared"),
                CreateEndpoint("aux-2")
            ]);

            using var driver = new Driver(hub, parserProvider, configurationProvider);

            Assert.True(driver.Detect());

            var detectedTablet = Assert.Single(driver.InputDevices);
            Assert.Equal(2, detectedTablet.InputDevices.Count);
            Assert.Equal(
                ["aux-2", "shared"],
                detectedTablet.InputDevices.Select(device => device.Endpoint.DevicePath).OrderBy(path => path).ToArray());
        }

        private static TabletConfiguration CreateTabletConfiguration()
        {
            return new TabletConfiguration
            {
                Name = "Multi Aux Test Tablet",
                Specifications = new TabletSpecifications(),
                DigitizerIdentifiers =
                [
                    CreateIdentifier("digitizer-1")
                ],
                AuxiliaryDeviceIdentifiers =
                [
                    CreateIdentifier("aux-1"),
                    CreateIdentifier("aux-2")
                ]
            };
        }

        private static TabletConfiguration CreateTabletConfigurationWithOverlappingAuxiliaryPath()
        {
            return new TabletConfiguration
            {
                Name = "Overlapping Aux Test Tablet",
                Specifications = new TabletSpecifications(),
                DigitizerIdentifiers =
                [
                    CreateIdentifier("shared")
                ],
                AuxiliaryDeviceIdentifiers =
                [
                    CreateIdentifier("shared"),
                    CreateIdentifier("aux-2")
                ]
            };
        }

        private static DeviceIdentifier CreateIdentifier(string devicePath) =>
            new()
            {
                VendorID = 1,
                ProductID = 2,
                ReportParser = "Test.Parser",
                Attributes = new Dictionary<string, string>
                {
                    ["DevicePath"] = $"^{devicePath}$"
                }
            };

        private static IDeviceEndpoint CreateEndpoint(string devicePath)
        {
            var endpoint = Substitute.For<IDeviceEndpoint>();
            endpoint.VendorID.Returns(1);
            endpoint.ProductID.Returns(2);
            endpoint.CanOpen.Returns(true);
            endpoint.DevicePath.Returns(devicePath);
            endpoint.DeviceAttributes.Returns(new Dictionary<string, string>());
            endpoint.FriendlyName.Returns(devicePath);
            endpoint.Open().Returns(new BlockingStream());
            endpoint.GetDeviceString(Arg.Any<byte>()).Returns(string.Empty);
            return endpoint;
        }

        private sealed class StubCompositeDeviceHub(IEnumerable<IDeviceEndpoint> devices) : ICompositeDeviceHub
        {
            private readonly IReadOnlyList<IDeviceEndpoint> _devices = devices.ToList();

            public event EventHandler<DevicesChangedEventArgs>? DevicesChanged;

            public IEnumerable<IDeviceHub> DeviceHubs => Array.Empty<IDeviceHub>();

            public IEnumerable<IDeviceEndpoint> GetDevices() => _devices;

            public void ConnectDeviceHub<T>() where T : IDeviceHub
            {
            }

            public void ConnectDeviceHub(IDeviceHub instance)
            {
            }

            public void DisconnectDeviceHub<T>() where T : IDeviceHub
            {
            }

            public void DisconnectDeviceHub(IDeviceHub instance)
            {
            }
        }

        private sealed class BlockingStream : IDeviceEndpointStream
        {
            private bool _disposed;

            public byte[] Read()
            {
                while (!_disposed)
                    System.Threading.Thread.Sleep(10);

                throw new IOException("I/O disconnected.");
            }

            public void Write(byte[] buffer)
            {
            }

            public void GetFeature(byte[] buffer)
            {
            }

            public void SetFeature(byte[] buffer)
            {
            }

            public void Dispose()
            {
                _disposed = true;
            }
        }
    }
}
