﻿namespace Microsoft.Azure.Devices.Applications.PredictiveMaintenance.Simulator.WebJob
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Common.Configurations;
    using Common.Models;
    using Common.Repository;
    using SimulatorCore.Devices;
    using SimulatorCore.Devices.Factory;
    using SimulatorCore.Logging;
    using SimulatorCore.Telemetry.Factory;
    using SimulatorCore.Transport.Factory;

    /// <summary>
    /// Creates multiple devices with events for testing.
    /// </summary>
    public class BulkDeviceTester
    {
        // change this to inject a different logger
        readonly ILogger _logger;
        readonly ITransportFactory _transportFactory;
        readonly IConfigurationProvider _configProvider;
        readonly ITelemetryFactory _telemetryFactory;
        readonly IDeviceFactory _deviceFactory;
        readonly IVirtualDeviceStorage _deviceStorage;

        List<InitialDeviceConfig> _deviceList;
        readonly int _devicePollIntervalSeconds;

        const int DEFAULT_DEVICE_POLL_INTERVAL_SECONDS = 120;

        public BulkDeviceTester(ITransportFactory transportFactory, ILogger logger, IConfigurationProvider configProvider,
            ITelemetryFactory telemetryFactory, IDeviceFactory deviceFactory, IVirtualDeviceStorage virtualDeviceStorage)
        {
            _transportFactory = transportFactory;
            _logger = logger;
            _configProvider = configProvider;
            _telemetryFactory = telemetryFactory;
            _deviceFactory = deviceFactory;
            _deviceStorage = virtualDeviceStorage;
            _deviceList = new List<InitialDeviceConfig>();

            string pollingIntervalString = _configProvider.GetConfigurationSettingValueOrDefault(
                "DevicePollIntervalSeconds",
                DEFAULT_DEVICE_POLL_INTERVAL_SECONDS.ToString(CultureInfo.InvariantCulture));

            _devicePollIntervalSeconds = Convert.ToInt32(pollingIntervalString, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Retrieves a set of device configs from the repository and creates devices with this information
        /// Once the devices are built, they are started
        /// </summary>
        /// <param name="token"></param>
        public async Task ProcessDevicesAsync(CancellationToken token)
        {
            var dm = new DeviceManager(_logger, token);

            try
            {
                _logger.LogInfo("********** Starting Simulator **********");
                while (!token.IsCancellationRequested)
                {
                    var newDevices = new List<InitialDeviceConfig>();
                    var removedDevices = new List<string>();
                    var devices = await _deviceStorage.GetDeviceListAsync();

                    if (devices != null && devices.Any())
                    {
                        newDevices = devices.Where(d => !_deviceList.Any(x => x.DeviceId == d.DeviceId)).ToList();
                        removedDevices =
                            _deviceList.Where(d => !devices.Any(x => x.DeviceId == d.DeviceId))
                                .Select(x => x.DeviceId)
                                .ToList();
                    }
                    else if (_deviceList != null && _deviceList.Any())
                    {
                        removedDevices = _deviceList.Select(x => x.DeviceId).ToList();
                    }

                    if (newDevices.Count > 0)
                    {
                        _logger.LogInfo("********** {0} NEW DEVICES FOUND ********** ", newDevices.Count);
                    }
                    if (removedDevices.Count > 0)
                    {
                        _logger.LogInfo("********** {0} DEVICES REMOVED ********** ", removedDevices.Count);
                    }

                    //reset the base list of devices for comparison the next
                    //time we retrieve the device list
                    _deviceList = devices;

                    if (removedDevices.Any())
                    {
                        //stop processing any devices that have been removed
                        dm.StopDevices(removedDevices);
                    }

                    //begin processing any new devices that were retrieved
                    if (newDevices.Any())
                    {
                        var devicesToProcess = new List<IDevice>();

                        foreach (var deviceConfig in newDevices)
                        {
                            _logger.LogInfo("********** SETTING UP NEW DEVICE : {0} ********** ", deviceConfig.DeviceId);
                            devicesToProcess.Add(_deviceFactory.CreateDevice(_logger, _transportFactory, _telemetryFactory, _configProvider, deviceConfig));
                        }

#pragma warning disable 4014
                        //don't wait for this to finish
                        dm.StartDevicesAsync(devicesToProcess);
#pragma warning restore 4014
                    }
                    await Task.Delay(TimeSpan.FromSeconds(_devicePollIntervalSeconds), token);
                }
            }
            catch (TaskCanceledException)
            {
                //do nothing if task was cancelled
                _logger.LogInfo("********** Primary worker role cancellation token source has been cancelled. **********");
            }
            finally
            {
                //ensure that all devices have been stopped
                dm.StopAllDevices();
            }
        }
    }
}