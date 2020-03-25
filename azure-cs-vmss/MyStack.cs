using Pulumi;
using Pulumi.Azure.Core;
using Pulumi.Azure.Compute;
using Pulumi.Azure.Compute.Inputs;
using Pulumi.Azure.Network;
using Pulumi.Azure.Network.Inputs;

class MyStack : Stack
{
    public MyStack()
    {
        // Retrieve options from config, optionally using defaults
        var config = new Pulumi.Config();
        var region = config.Get("azure-cs-vmss:region") ?? "CentralUS";
        // App Gateway Options
        InputList<string> addressSpace = (config.Get("addressSpace") ?? "10.0.0.0/16").Split(',');
        var privateSubnetPrefix = config.Get("privateSubnet") ?? "10.0.2.0/24";
        var publicSubnetPrefix = config.Get("publicSubnet") ?? "10.0.1.0/24";
        var dnsPrefix = config.Get("dnsPrefix") ?? "aspnettodo";
        var backendPort = config.GetInt32("backendPort") ?? 80;
        var backendProtocol = config.Get("backendProtocol") ?? "HTTP";
        var frontendPort = config.GetInt32("frontendPort") ?? backendPort;
        var frontendProtocol = config.Get("frontendProtocol") ?? backendProtocol;
        // VMSS options
        var instanceCount = config.GetInt32("instanceCount") ?? 2;
        InputList<string> zones =  (config.Get("zones") ?? "1,2").Split(',');
        var instanceSize = config.Get("instanceSize") ?? "Standard_B1s";
        var instanceNamePrefix = config.Get("instanceNamePrefix") ?? "web";
        var adminUser = config.Get("adminUser") ?? "webadmin";
        var adminPassword = config.Get("adminPassword");

        // Create an Azure Resource Group
        var resourceGroup = new ResourceGroup($"{stackId}-rg", new ResourceGroupArgs
        {
            Location = region
        });

        // Create Networking components
        var vnet = new VirtualNetwork($"{stackId}-vnet", new VirtualNetworkArgs
        {
            ResourceGroupName = resourceGroup.Name,
            AddressSpaces = addressSpace
        });

        // Create a private subnet for the VMSS
        var privateSubnet = new Subnet($"{stackId}-privateSubnet", new SubnetArgs
        {
            ResourceGroupName = resourceGroup.Name,
            AddressPrefix = privateSubnetPrefix,
            VirtualNetworkName = vnet.Name
        });

        // Create a public subnet for the Application Gateway
        var publicSubnet = new Subnet($"{stackId}-publicSubnet", new SubnetArgs
        {
            ResourceGroupName = resourceGroup.Name,
            AddressPrefix = publicSubnetPrefix,
            VirtualNetworkName = vnet.Name
        });

        // Create a public IP and App Gateway
        var publicIp = new PublicIp($"{stackId}-pip", new PublicIpArgs
        {
            ResourceGroupName = resourceGroup.Name,
            Sku = "Basic",
            AllocationMethod = "Dynamic",
            DomainNameLabel = dnsPrefix
        }, new CustomResourceOptions { DeleteBeforeReplace = true });

        var appGw = new ApplicationGateway($"{stackId}-appgw", new ApplicationGatewayArgs
        {
            ResourceGroupName = resourceGroup.Name,
            Sku = new ApplicationGatewaySkuArgs
            {
                Tier = "Standard",
                Name = "Standard_Small",
                Capacity = 1
            },
            FrontendIpConfigurations = new InputList<Pulumi.Azure.Network.Inputs.ApplicationGatewayFrontendIpConfigurationsArgs>
            {
                new Pulumi.Azure.Network.Inputs.ApplicationGatewayFrontendIpConfigurationsArgs
                {
                    Name = $"{stackId}-appgw-ipconfig-0",
                    PublicIpAddressId = publicIp.Id,
                }
            },
            FrontendPorts = new InputList<Pulumi.Azure.Network.Inputs.ApplicationGatewayFrontendPortsArgs>
            {
                new Pulumi.Azure.Network.Inputs.ApplicationGatewayFrontendPortsArgs
                {
                    Name = $"Port{frontendPort}",
                    Port = frontendPort
                }
            },
            BackendAddressPools = new InputList<Pulumi.Azure.Network.Inputs.ApplicationGatewayBackendAddressPoolsArgs>
            {
                new Pulumi.Azure.Network.Inputs.ApplicationGatewayBackendAddressPoolsArgs
                {
                    Name = $"{stackId}-bepool-0",
                }
            },
            BackendHttpSettings = new InputList<ApplicationGatewayBackendHttpSettingsArgs>
            {
                new ApplicationGatewayBackendHttpSettingsArgs
                {
                    Name = $"{backendProtocol}Settings",
                    Protocol = backendProtocol,
                    Port = backendPort,
                    CookieBasedAffinity = "Disabled"
                }
            },
            GatewayIpConfigurations = new InputList<ApplicationGatewayGatewayIpConfigurationsArgs>
            {
                new ApplicationGatewayGatewayIpConfigurationsArgs
                {
                    Name = "IPConfiguration",
                    SubnetId = publicSubnet.Id
                }
            },
            HttpListeners = new InputList<ApplicationGatewayHttpListenersArgs>
            {
                new ApplicationGatewayHttpListenersArgs
                {
                    Name = $"{frontendProtocol}Listener",
                    Protocol = frontendProtocol,
                    FrontendIpConfigurationName = $"{stackId}-appgw-ipconfig-0",
                    FrontendPortName = $"Port{frontendPort}"
                }
            },
            RequestRoutingRules = new InputList<ApplicationGatewayRequestRoutingRulesArgs>
            {
                new ApplicationGatewayRequestRoutingRulesArgs
                {
                    Name = "Default",
                    BackendAddressPoolName = $"{stackId}-bepool-0",
                    HttpListenerName = $"{frontendProtocol}Listener",
                    RuleType = "Basic",
                    BackendHttpSettingsName = $"{backendProtocol}Settings"
                }
            }

        });

        // Create the scale set
        var scaleSet = new ScaleSet($"{stackId}-vmss", new ScaleSetArgs
        {
            ResourceGroupName = resourceGroup.Name,
            Location = resourceGroup.Location,
            Zones = new InputList<string> {
                "1","2"
            },
            NetworkProfiles =
            {
                new ScaleSetNetworkProfilesArgs
                {
                    AcceleratedNetworking = false,
                    IpConfigurations =
                    {
                        new ScaleSetNetworkProfilesIpConfigurationsArgs
                        {
                            Name = "IPConfiguration",
                            Primary = true,
                            SubnetId = privateSubnet.Id,
                            // Associate scaleset with app gateway
                            ApplicationGatewayBackendAddressPoolIds = new InputList<string>
                            {
                                appGw.BackendAddressPools.Apply(bePools => bePools[0].Id)
                            }
                        }
                    },
                    Name = "networkprofile",
                    Primary = true
                }
            },
            OsProfile = new ScaleSetOsProfileArgs
            {
                AdminUsername = adminUser,
                AdminPassword = adminPassword,
                ComputerNamePrefix = instanceNamePrefix
            },
            Sku = new ScaleSetSkuArgs
            {
                Capacity = instanceCount,
                Name = instanceSize,
                Tier = "Standard",
            },
            StorageProfileImageReference = new ScaleSetStorageProfileImageReferenceArgs
            {
                Offer = "WindowsServer",
                Publisher = "MicrosoftWindowsServer",
                Sku = "2019-Datacenter-Core",
                Version = "latest",
            },
            StorageProfileOsDisk = new ScaleSetStorageProfileOsDiskArgs
            {
                Caching = "ReadWrite",
                CreateOption = "FromImage",
                ManagedDiskType = "Standard_LRS",
                Name = "",
            },
            // Enable VM agent and script extension
            UpgradePolicyMode = "Automatic",
            OsProfileWindowsConfig = new ScaleSetOsProfileWindowsConfigArgs
            {
                ProvisionVmAgent = true
            },
            Extensions = new InputList<ScaleSetExtensionsArgs>
            {
                new ScaleSetExtensionsArgs
                {
                    Publisher = "Microsoft.Compute",
                    Name = "IIS-Script-Extension",
                    Type = "CustomScriptExtension",
                    TypeHandlerVersion = "1.4",
                    // Settings is a JSON string
                    // This command uses powershell to install windows webserver features
                    Settings = "{\"commandToExecute\":\"powershell Add-WindowsFeature Web-Server,Web-Asp-Net45,NET-Framework-Features\"}"
                }
            }
        });

        this.PublicUrl = publicIp.Fqdn;
    }

    // Set a string to use as a prefix on all resources
    private const string stackId = "webScaleSet";
    
    // Define Output string to hold the public url of created resources
    [Output]
    public Output<string> PublicUrl { get; set; }
}
