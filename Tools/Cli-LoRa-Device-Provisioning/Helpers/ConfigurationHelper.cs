// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWan.Tools.CLI.Helpers
{
    using System;
    using System.Globalization;
    using System.IO;
    using Azure.Storage.Blobs;
    using Microsoft.Azure.Devices;
    using Microsoft.Extensions.Configuration;

    internal class ConfigurationHelper
    {
        private const string CredentialsStorageContainerName = "stationcredentials";
        public string NetId { get; set; }

        public RegistryManager RegistryManager { get; set; }
        public BlobContainerClient CertificateStorageContainerClient { get; set; }

        public bool ReadConfig()
        {
            string connectionString, netId, credentialStorageConnectionString;

            Console.WriteLine("Reading configuration file \"appsettings.json\"...");

            // Read configuration file appsettings.json
            try
            {
                var configurationBuilder = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                    .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false)
                    .Build();

                connectionString = configurationBuilder["IoTHubConnectionString"];
                credentialStorageConnectionString = configurationBuilder["CredentialStorageConnectionString"];
                netId = configurationBuilder["NetId"];

                if (connectionString is null || credentialStorageConnectionString is null || netId is null)
                {
                    StatusConsole.WriteLogLine(MessageType.Info, "The file should have the following structure: { \"IoTHubConnectionString\" : \"HostName=xxx.azure-devices.net;SharedAccessKeyName=iothubowner;SharedAccessKey=xxx\" }");
                    return false;
                }
            }
            catch (FileNotFoundException)
            {
                StatusConsole.WriteLogLine(MessageType.Error, "Configuration file 'appsettings.json' was not found.");
                return false;
            }

            // Validate connection setting
            if (string.IsNullOrEmpty(connectionString))
            {
                StatusConsole.WriteLogLine(MessageType.Error, "Connection string 'IoTHubConnectionString' may not be empty.");
                return false;
            }
            else
            {
                // Just show IoT Hub Hostname
                if (GetHostFromConnectionString(connectionString, out var hostName))
                {
                    StatusConsole.WriteLogLine(MessageType.Info, $"Using IoT Hub: {hostName}");
                }
                else
                {
                    StatusConsole.WriteLogLine(MessageType.Error, "Invalid connection string in appsettings.json. Can not find \"HostName=\". The file should have the following structure: { \"IoTHubConnectionString\" : \"HostName=xxx.azure-devices.net;SharedAccessKeyName=iothubowner;SharedAccessKey=xxx\" }.");
                    return false;
                }
            }

            // Validate NetId setting
            if (string.IsNullOrEmpty(netId))
            {
                netId = ValidationHelper.CleanNetId(Constants.DefaultNetId.ToString(CultureInfo.InvariantCulture));
                StatusConsole.WriteLogLine(MessageType.Info, $"NetId is not set in settings file. Using default {netId}.");
            }
            else
            {
                netId = ValidationHelper.CleanNetId(netId);

                if (ValidationHelper.ValidateHexStringTwinProperty(netId, 3, out var customError))
                {
                    StatusConsole.WriteLogLine(MessageType.Info, $"Using NetId {netId} from settings file.");
                }
                else
                {
                    var netIdBad = netId;
                    netId = ValidationHelper.CleanNetId(Constants.DefaultNetId.ToString(CultureInfo.InvariantCulture));
                    StatusConsole.WriteLogLine(MessageType.Warning, $"NetId {netIdBad} in settings file is invalid. {customError}.");
                    StatusConsole.WriteLogLine(MessageType.Warning, $"Using default NetId {netId} instead.");
                }
            }

            StatusConsole.WriteLogLine(MessageType.Info, $"To override, use --netid parameter.");

            NetId = netId;

            // Create Registry Manager using connection string
            try
            {
                RegistryManager = RegistryManager.CreateFromConnectionString(connectionString);
            }
            catch (Exception ex) when (ex is ArgumentNullException
                                          or FormatException
                                          or ArgumentException)
            {
                StatusConsole.WriteLogLine(MessageType.Error, $"Failed to create connection string: {ex.Message}.");
                return false;
            }

            try
            {
                CertificateStorageContainerClient = new BlobContainerClient(credentialStorageConnectionString, CredentialsStorageContainerName);
            }
            catch (FormatException)
            {
                StatusConsole.WriteLogLine(MessageType.Info, "Credentials storage account is incorrectly configured.");
            }

            Console.WriteLine("done.");
            return true;
        }

        public static bool GetHostFromConnectionString(string connectionString, out string hostName)
        {
            hostName = string.Empty;

            var from = connectionString.IndexOf("HostName=", StringComparison.OrdinalIgnoreCase);
            var fromOffset = "HostName=".Length;

            var to = connectionString.IndexOf("azure-devices.net", StringComparison.OrdinalIgnoreCase);
            var length = to - from - fromOffset + "azure-devices.net".Length;

            if (from == -1 || to == -1)
            {
                return false;
            }
            else
            {
                hostName = connectionString.Substring(from + fromOffset, length);
                return true;
            }
        }
    }
}